using System.Text.Json;
using System.Text.RegularExpressions;
using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    // Internal lifecycle events used for navigation waits
    internal event Action? LoadEventFired;
    internal event Action? DomContentEventFired;

    // Raised with the id of a frame that has finished loading. The only lifecycle signal a
    // subframe produces, so it is what frame navigation waits on.
    internal event Action<string>? FrameStoppedLoading;

    public async Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null)
    {
        if (_context.BaseURL is not null && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            url = new Uri(new Uri(_context.BaseURL), url).ToString();

        await _context.LifecycleHooks.FireBeforeNavigationAsync(this, url).ConfigureAwait(false);

        var waitUntil = options?.WaitUntil ?? WaitUntil.Load;
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);

        var waiter = CreateLifecycleWaiter(waitUntil, timeout);

        var result = await _session.SendAsync(
            "Page.navigate",
            new PageNavigateParams(url, Referrer: options?.Referer),
            CdpJsonContext.Default.PageNavigateParams,
            CdpJsonContext.Default.PageNavigateResult,
            _pageCts.Token).ConfigureAwait(false);

        if (result.ErrorText is not null)
            throw new MotusNavigationException(url, errorCode: result.ErrorText, pageUrl: Url);

        await waiter.ConfigureAwait(false);

        IResponse? response = _networkManager?.GetLastNavigationResponse();
        await _context.LifecycleHooks.FireAfterNavigationAsync(this, response).ConfigureAwait(false);
        return response;
    }

    public async Task<IResponse?> GoBackAsync(NavigationOptions? options = null)
    {
        var history = await _session.SendAsync(
            "Page.getNavigationHistory",
            CdpJsonContext.Default.PageGetNavigationHistoryResult,
            _pageCts.Token).ConfigureAwait(false);

        if (history.CurrentIndex <= 0)
            return null;

        var entry = history.Entries[history.CurrentIndex - 1];
        await _context.LifecycleHooks.FireBeforeNavigationAsync(this, entry.Url).ConfigureAwait(false);

        var waitUntil = options?.WaitUntil ?? WaitUntil.Load;
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);

        var waiter = CreateLifecycleWaiter(waitUntil, timeout);

        await _session.SendAsync(
            "Page.navigateToHistoryEntry",
            new PageNavigateToHistoryEntryParams(entry.Id),
            CdpJsonContext.Default.PageNavigateToHistoryEntryParams,
            CdpJsonContext.Default.PageNavigateToHistoryEntryResult,
            _pageCts.Token).ConfigureAwait(false);

        await waiter.ConfigureAwait(false);

        IResponse? response = _networkManager?.GetLastNavigationResponse();
        await _context.LifecycleHooks.FireAfterNavigationAsync(this, response).ConfigureAwait(false);
        return response;
    }

    public async Task<IResponse?> GoForwardAsync(NavigationOptions? options = null)
    {
        var history = await _session.SendAsync(
            "Page.getNavigationHistory",
            CdpJsonContext.Default.PageGetNavigationHistoryResult,
            _pageCts.Token).ConfigureAwait(false);

        if (history.CurrentIndex >= history.Entries.Length - 1)
            return null;

        var entry = history.Entries[history.CurrentIndex + 1];
        await _context.LifecycleHooks.FireBeforeNavigationAsync(this, entry.Url).ConfigureAwait(false);

        var waitUntil = options?.WaitUntil ?? WaitUntil.Load;
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);

        var waiter = CreateLifecycleWaiter(waitUntil, timeout);

        await _session.SendAsync(
            "Page.navigateToHistoryEntry",
            new PageNavigateToHistoryEntryParams(entry.Id),
            CdpJsonContext.Default.PageNavigateToHistoryEntryParams,
            CdpJsonContext.Default.PageNavigateToHistoryEntryResult,
            _pageCts.Token).ConfigureAwait(false);

        await waiter.ConfigureAwait(false);

        IResponse? response = _networkManager?.GetLastNavigationResponse();
        await _context.LifecycleHooks.FireAfterNavigationAsync(this, response).ConfigureAwait(false);
        return response;
    }

    public async Task<IResponse?> ReloadAsync(NavigationOptions? options = null)
    {
        await _context.LifecycleHooks.FireBeforeNavigationAsync(this, Url).ConfigureAwait(false);

        var waitUntil = options?.WaitUntil ?? WaitUntil.Load;
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);

        var waiter = CreateLifecycleWaiter(waitUntil, timeout);

        await _session.SendAsync(
            "Page.reload",
            CdpJsonContext.Default.PageReloadResult,
            _pageCts.Token).ConfigureAwait(false);

        await waiter.ConfigureAwait(false);

        IResponse? response = _networkManager?.GetLastNavigationResponse();
        await _context.LifecycleHooks.FireAfterNavigationAsync(this, response).ConfigureAwait(false);
        return response;
    }

    public async Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null)
    {
        var loadState = state ?? LoadState.Load;
        var timeoutMs = timeout ?? 30_000;

        if (loadState == LoadState.NetworkIdle)
        {
            if (_networkManager is null)
                throw new InvalidOperationException("NetworkManager is not initialized.");
            await _networkManager.WaitForNetworkIdleAsync(
                TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
            return;
        }

        var waitUntil = loadState == LoadState.DOMContentLoaded
            ? WaitUntil.DOMContentLoaded
            : WaitUntil.Load;

        await CreateLifecycleWaiter(waitUntil, TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
    }

    public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(urlPattern);
        return WaitForFrameUrlAsync(
            frame: null, url => UrlMatchesWhole(url, urlPattern), $"'{urlPattern}'", options);
    }

    public Task WaitForURLAsync(Regex urlPattern, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(urlPattern);
        return WaitForFrameUrlAsync(
            frame: null, urlPattern.IsMatch, $"/{urlPattern}/", options);
    }

    public Task WaitForURLAsync(Func<string, bool> predicate, NavigationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return WaitForFrameUrlAsync(frame: null, predicate, "a predicate", options);
    }

    /// <summary>
    /// Waits until a frame's URL satisfies <paramref name="matches"/>, returning at once when it
    /// already does.
    /// </summary>
    /// <param name="frame">The frame to watch, or <see langword="null"/> for the main frame.</param>
    /// <param name="matches">Decides whether a URL is the one being waited for.</param>
    /// <param name="description">How the expected URL reads in a timeout message.</param>
    /// <param name="options">The timeout; navigation options other than that are unused.</param>
    /// <remarks>
    /// Every URL the frame navigates to is tested, including ones it passes through on the way
    /// somewhere else and ones reached without loading a new document (the history API, or a
    /// fragment), so a wait does not miss a URL that was current for less than a polling
    /// interval. The current URL is polled as well, which covers a change no event was raised
    /// for. <paramref name="matches"/> is only ever called from the waiting loop, never from the
    /// thread delivering events, so a caller's predicate is not run concurrently with itself.
    /// </remarks>
    internal async Task WaitForFrameUrlAsync(
        Frame? frame, Func<string, bool> matches, string description, NavigationOptions? options)
    {
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);
        string CurrentUrl() => frame?.Url ?? Url;

        var visited = new System.Collections.Concurrent.ConcurrentQueue<string>();

        // Not disposed: an event already being raised when the handler is removed may still
        // release it, and a semaphore with no wait handle holds nothing that needs freeing.
        var signal = new SemaphoreSlim(0);

        void OnNavigated(object? sender, IFrame navigated)
        {
            var watched = frame is null
                ? navigated is Frame f && f.Id == _mainFrameId
                : ReferenceEquals(navigated, frame);
            if (!watched)
                return;

            visited.Enqueue(navigated.Url);
            signal.Release();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
        cts.CancelAfter(timeout);

        // Subscribed before the first check, so a navigation landing in between is still seen.
        FrameNavigated += OnNavigated;
        try
        {
            if (matches(CurrentUrl()))
                return;

            while (true)
            {
                try
                {
                    await signal.WaitAsync(100, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!_pageCts.IsCancellationRequested)
                {
                    break;
                }

                while (visited.TryDequeue(out var url))
                {
                    if (matches(url))
                        return;
                }

                if (matches(CurrentUrl()))
                    return;
            }
        }
        finally
        {
            FrameNavigated -= OnNavigated;
        }

        var lastUrl = CurrentUrl();
        throw new WaitTimeoutException(
            condition: $"URL match {description}",
            timeoutDuration: timeout,
            lastEvaluatedValue: lastUrl,
            message: $"WaitForURL timed out waiting for {description} after {timeout.TotalMilliseconds}ms. "
                + $"The URL was '{lastUrl}'.");
    }

    public async Task WaitForTimeoutAsync(double timeout) =>
        await Task.Delay(TimeSpan.FromMilliseconds(timeout), _pageCts.Token).ConfigureAwait(false);

    private Task CreateLifecycleWaiter(WaitUntil waitUntil, TimeSpan timeout)
    {
        if (waitUntil == WaitUntil.NetworkIdle)
        {
            if (_networkManager is null)
                throw new InvalidOperationException("NetworkManager is not initialized.");
            return _networkManager.WaitForNetworkIdleAsync(
                TimeSpan.FromMilliseconds(500), timeout);
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource(timeout);

        cts.Token.Register(() => tcs.TrySetException(
            new NavigationTimeoutException(
                url: string.Empty, timeoutDuration: timeout, lastNetworkEvents: null,
                message: $"Navigation timed out after {timeout.TotalMilliseconds}ms.")));

        if (waitUntil == WaitUntil.DOMContentLoaded)
        {
            void Handler()
            {
                DomContentEventFired -= Handler;
                cts.Dispose();
                tcs.TrySetResult();
            }

            DomContentEventFired += Handler;
        }
        else
        {
            void Handler()
            {
                LoadEventFired -= Handler;
                cts.Dispose();
                tcs.TrySetResult();
            }

            LoadEventFired += Handler;
        }

        return tcs.Task;
    }

    /// <summary>
    /// Navigates a single frame, leaving the rest of the page where it is.
    /// </summary>
    /// <remarks>
    /// A subframe has no lifecycle events of its own beyond <c>Page.frameStoppedLoading</c>, so
    /// <see cref="WaitUntil.DOMContentLoaded"/> and <see cref="WaitUntil.Load"/> both resolve on
    /// that one signal. <see cref="WaitUntil.NetworkIdle"/> remains page-wide, because request
    /// tracking is not partitioned by frame.
    /// </remarks>
    internal async Task<IResponse?> GotoInFrameAsync(
        string frameId, string url, NavigationOptions? options = null)
    {
        if (_context.BaseURL is not null && !Uri.IsWellFormedUriString(url, UriKind.Absolute))
            url = new Uri(new Uri(_context.BaseURL), url).ToString();

        await _context.LifecycleHooks.FireBeforeNavigationAsync(this, url).ConfigureAwait(false);

        var waitUntil = options?.WaitUntil ?? WaitUntil.Load;
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);

        await WhenFrameReadyAsync(frameId).ConfigureAwait(false);

        var waiter = CreateFrameLifecycleWaiter(frameId, waitUntil, timeout);

        _frames.TryGetValue(frameId, out var frame);

        var result = await SessionFor(frame).SendAsync(
            "Page.navigate",
            new PageNavigateParams(url, Referrer: options?.Referer, FrameId: frameId),
            CdpJsonContext.Default.PageNavigateParams,
            CdpJsonContext.Default.PageNavigateResult,
            _pageCts.Token).ConfigureAwait(false);

        if (result.ErrorText is not null)
            throw new MotusNavigationException(url, errorCode: result.ErrorText, pageUrl: Url);

        await waiter.ConfigureAwait(false);

        IResponse? response = _networkManager?.GetLastNavigationResponse();
        await _context.LifecycleHooks.FireAfterNavigationAsync(this, response).ConfigureAwait(false);
        return response;
    }

    private Task CreateFrameLifecycleWaiter(string frameId, WaitUntil waitUntil, TimeSpan timeout)
    {
        if (waitUntil == WaitUntil.NetworkIdle)
        {
            if (_networkManager is null)
                throw new InvalidOperationException("NetworkManager is not initialized.");
            return _networkManager.WaitForNetworkIdleAsync(
                TimeSpan.FromMilliseconds(500), timeout);
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cts = new CancellationTokenSource(timeout);

        cts.Token.Register(() => tcs.TrySetException(
            new NavigationTimeoutException(
                url: string.Empty, timeoutDuration: timeout, lastNetworkEvents: null,
                message: $"Frame navigation timed out after {timeout.TotalMilliseconds}ms.")));

        void Handler(string stoppedFrameId)
        {
            if (!string.Equals(stoppedFrameId, frameId, StringComparison.Ordinal))
                return;

            FrameStoppedLoading -= Handler;
            cts.Dispose();
            tcs.TrySetResult();
        }

        FrameStoppedLoading += Handler;

        return tcs.Task;
    }

    /// <summary>
    /// Matches a request URL the way routes and request or response waits do: an exact URL, a
    /// glob over the whole URL when the pattern contains <c>*</c>, and otherwise any URL that
    /// contains the pattern.
    /// </summary>
    internal static bool UrlMatchesStatic(string url, string pattern) =>
        UrlMatches(url, pattern);

    private static bool UrlMatches(string url, string pattern)
    {
        if (pattern == url)
            return true;

        if (pattern.Contains('*'))
            return GlobMatches(url, pattern);

        return url.Contains(pattern, StringComparison.Ordinal);
    }

    /// <summary>
    /// Matches the URL of a page or frame: a pattern containing <c>*</c> is a glob that has to
    /// cover the whole URL, and any other pattern has to equal the URL exactly.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="UrlMatchesStatic"/> there is no substring fallback. A site's root is a
    /// prefix of every URL on the site, and <c>?page=1</c> is a prefix of <c>?page=10</c>, so a
    /// containment check answered "yes" for pages the caller was not waiting for.
    /// <para>
    /// A browser reports a URL in its normal form, so <c>https://example.com</c> comes back as
    /// <c>https://example.com/</c> and a host in lower case. An http or https pattern written the
    /// short way names the same address, so it is also compared in that form; without that, a
    /// wait for a site's base URL as it is usually written would never match.
    /// </para>
    /// </remarks>
    internal static bool UrlMatchesWhole(string url, string pattern)
    {
        if (pattern.Contains('*'))
            return GlobMatches(url, pattern);

        if (string.Equals(url, pattern, StringComparison.Ordinal))
            return true;

        return pattern.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(pattern, UriKind.Absolute, out var parsed)
            && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            && string.Equals(url, parsed.AbsoluteUri, StringComparison.Ordinal);
    }

    // Each * stands for any run of characters, path separators included, so * and ** are the same.
    // Everything else in the pattern is literal, and the glob has to cover the whole URL.
    private static bool GlobMatches(string url, string pattern)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
        return Regex.IsMatch(url, regex);
    }
}
