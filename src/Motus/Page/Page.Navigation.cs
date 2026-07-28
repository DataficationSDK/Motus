using System.Text.Json;
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

    public async Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null)
    {
        var timeout = TimeSpan.FromMilliseconds(options?.Timeout ?? 30_000);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (UrlMatches(Url, urlPattern))
                return;

            await Task.Delay(100, cts.Token).ConfigureAwait(false);
        }

        throw new WaitTimeoutException(
            condition: $"URL match '{urlPattern}'",
            timeoutDuration: timeout,
            lastEvaluatedValue: Url,
            message: $"WaitForURL timed out waiting for '{urlPattern}' after {timeout.TotalMilliseconds}ms.");
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

    internal static bool UrlMatchesStatic(string url, string pattern) =>
        UrlMatches(url, pattern);

    private static bool UrlMatches(string url, string pattern)
    {
        if (pattern == url)
            return true;

        if (pattern.Contains('*'))
        {
            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
                .Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(url, regex);
        }

        return url.Contains(pattern, StringComparison.Ordinal);
    }
}
