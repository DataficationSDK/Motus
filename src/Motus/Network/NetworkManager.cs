using System.Collections.Concurrent;
using System.Text.Json.Serialization.Metadata;
using Motus.Abstractions;

namespace Motus;

internal sealed class NetworkManager
{
    private readonly CdpSession _session;
    private readonly Page _page;
    private readonly CancellationToken _ct;

    private readonly ConcurrentDictionary<string, MotusRequest> _requests = new();
    private readonly ConcurrentDictionary<string, MotusResponse> _responses = new();

    private int _activeRequests;
    private readonly object _idleLock = new();
    private TaskCompletionSource? _idleTcs;

    private readonly List<(string Pattern, TaskCompletionSource<IRequest> Tcs)> _requestWaiters = [];
    private readonly List<(string Pattern, TaskCompletionSource<IResponse> Tcs)> _responseWaiters = [];
    private readonly object _waiterLock = new();

    private MotusResponse? _lastNavigationResponse;
    private bool _fetchEnabled;

    internal NetworkManager(CdpSession session, Page page, CancellationToken ct)
    {
        _session = session;
        _page = page;
        _ct = ct;
    }

    internal async Task InitializeAsync(bool hasFetchRoutes, CancellationToken ct)
    {
        await _session.SendAsync(
            "Network.enable",
            new NetworkEnableParams(),
            CdpJsonContext.Default.NetworkEnableParams,
            CdpJsonContext.Default.NetworkEnableResult,
            ct).ConfigureAwait(false);

        StartNetworkEventPump();

        if (hasFetchRoutes)
            await EnableFetchAsync(ct).ConfigureAwait(false);
    }

    internal async Task EnableFetchAsync(CancellationToken ct)
    {
        if (_fetchEnabled)
            return;

        _fetchEnabled = true;

        await _session.SendAsync(
            "Fetch.enable",
            new FetchEnableParams(
                Patterns: [new FetchRequestPattern(UrlPattern: "*", RequestStage: "Request")]),
            CdpJsonContext.Default.FetchEnableParams,
            CdpJsonContext.Default.FetchEnableResult,
            ct).ConfigureAwait(false);

        StartFetchEventPump();
    }

    internal async Task EnableFetchWithAuthAsync(CancellationToken ct)
    {
        if (_fetchEnabled)
            return;

        _fetchEnabled = true;

        await _session.SendAsync(
            "Fetch.enable",
            new FetchEnableParams(
                Patterns: [new FetchRequestPattern(UrlPattern: "*", RequestStage: "Request")],
                HandleAuthRequests: true),
            CdpJsonContext.Default.FetchEnableParams,
            CdpJsonContext.Default.FetchEnableResult,
            ct).ConfigureAwait(false);

        StartFetchEventPump();
    }

    internal async Task DisableFetchAsync(CancellationToken ct)
    {
        if (!_fetchEnabled)
            return;

        _fetchEnabled = false;

        await _session.SendAsync(
            "Fetch.disable",
            CdpJsonContext.Default.FetchDisableResult,
            ct).ConfigureAwait(false);
    }

    internal IResponse? GetLastNavigationResponse() => _lastNavigationResponse;

    internal void ClearLastNavigationResponse() => _lastNavigationResponse = null;

    internal Task<IRequest> WaitForRequestAsync(string urlPattern, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<IRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiterLock)
            _requestWaiters.Add((urlPattern, tcs));

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        cts.CancelAfter(timeout);
        cts.Token.Register(() => tcs.TrySetException(
            new TimeoutException($"WaitForRequest timed out after {timeout.TotalMilliseconds}ms.")));

        return tcs.Task;
    }

    internal Task<IResponse> WaitForResponseAsync(string urlPattern, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource<IResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiterLock)
            _responseWaiters.Add((urlPattern, tcs));

        var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        cts.CancelAfter(timeout);
        cts.Token.Register(() => tcs.TrySetException(
            new TimeoutException($"WaitForResponse timed out after {timeout.TotalMilliseconds}ms.")));

        return tcs.Task;
    }

    internal async Task WaitForNetworkIdleAsync(TimeSpan idlePeriod, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(_ct);
        cts.CancelAfter(timeout);

        while (!cts.Token.IsCancellationRequested)
        {
            if (Volatile.Read(ref _activeRequests) <= 0)
            {
                // Wait for the idle period; if a new request starts, the counter rises and we retry
                try
                {
                    await Task.Delay(idlePeriod, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                if (Volatile.Read(ref _activeRequests) <= 0)
                    return;
            }
            else
            {
                // Wait for requests to finish
                var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (_idleLock)
                    _idleTcs = tcs;

                using var reg = cts.Token.Register(() => tcs.TrySetCanceled());
                try
                {
                    await tcs.Task.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        cts.Token.ThrowIfCancellationRequested();
    }

    private void StartNetworkEventPump()
    {
        _ = PumpAsync("Network.requestWillBeSent",
            CdpJsonContext.Default.NetworkRequestWillBeSentEvent,
            OnRequestWillBeSent, _ct);

        _ = PumpAsync("Network.responseReceived",
            CdpJsonContext.Default.NetworkResponseReceivedEvent,
            OnResponseReceived, _ct);

        _ = PumpAsync("Network.loadingFinished",
            CdpJsonContext.Default.NetworkLoadingFinishedEvent,
            OnLoadingFinished, _ct);

        _ = PumpAsync("Network.loadingFailed",
            CdpJsonContext.Default.NetworkLoadingFailedEvent,
            OnLoadingFailed, _ct);
    }

    private void StartFetchEventPump()
    {
        _ = PumpAsync("Fetch.requestPaused",
            CdpJsonContext.Default.FetchRequestPausedEvent,
            OnFetchRequestPaused, _ct);
    }

    private async Task PumpAsync<T>(
        string eventName,
        JsonTypeInfo<T> typeInfo,
        Action<T> handler,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _session.SubscribeAsync(eventName, typeInfo, ct).ConfigureAwait(false))
            {
                try { handler(evt); }
                catch { /* swallow handler errors to keep pump alive */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void OnRequestWillBeSent(NetworkRequestWillBeSentEvent evt)
    {
        var frame = ResolveFrame(evt.FrameId);
        var isNavigation = string.Equals(evt.Type, "Document", StringComparison.OrdinalIgnoreCase);
        var request = new MotusRequest(
            evt.Request.Url,
            evt.Request.Method,
            evt.Request.Headers,
            evt.Request.PostData,
            evt.Type ?? "Other",
            isNavigation,
            frame);

        _requests[evt.RequestId] = request;
        Interlocked.Increment(ref _activeRequests);

        _page.FireRequest(request);
        NotifyRequestWaiters(request);
    }

    private void OnResponseReceived(NetworkResponseReceivedEvent evt)
    {
        if (!_requests.TryGetValue(evt.RequestId, out var request))
            return;
        if (evt.Response is null)
            return;

        var frame = ResolveFrame(evt.FrameId);
        var response = new MotusResponse(
            evt.Response.Url,
            evt.Response.Status,
            evt.Response.StatusText,
            evt.Response.Headers,
            request,
            frame,
            _session,
            evt.RequestId);

        _responses[evt.RequestId] = response;
        request.SetResponse(response);

        if (request.IsNavigationRequest)
            _lastNavigationResponse = response;

        _page.FireResponse(response);
        NotifyResponseWaiters(response);
    }

    private void OnLoadingFinished(NetworkLoadingFinishedEvent evt)
    {
        if (_requests.TryGetValue(evt.RequestId, out var request))
        {
            _page.FireRequestFinished(request);
            DecrementActiveAndCheckIdle();
        }
    }

    private void OnLoadingFailed(NetworkLoadingFailedEvent evt)
    {
        if (_requests.TryGetValue(evt.RequestId, out var request))
        {
            _page.FireRequestFailed(request);
            DecrementActiveAndCheckIdle();
        }
    }

    private void OnFetchRequestPaused(FetchRequestPausedEvent evt)
    {
        var url = evt.Request.Url;
        var (_, handler) = _page.FindRouteHandler(url);

        if (handler is null)
        {
            _ = AutoContinueAsync(evt.RequestId);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var frame = ResolveFrame(evt.FrameId);
                var request = new MotusRequest(
                    evt.Request.Url,
                    evt.Request.Method,
                    evt.Request.Headers,
                    evt.Request.PostData,
                    evt.ResourceType,
                    string.Equals(evt.ResourceType, "Document", StringComparison.OrdinalIgnoreCase),
                    frame);

                var route = new MotusRoute(request, _session, evt.RequestId);
                await handler(route).ConfigureAwait(false);

                if (!route.IsHandled)
                    await AutoContinueAsync(evt.RequestId).ConfigureAwait(false);
            }
            catch
            {
                await AutoContinueAsync(evt.RequestId).ConfigureAwait(false);
            }
        });
    }

    private async Task AutoContinueAsync(string requestId)
    {
        try
        {
            await _session.SendAsync(
                "Fetch.continueRequest",
                new FetchContinueRequestParams(requestId),
                CdpJsonContext.Default.FetchContinueRequestParams,
                CdpJsonContext.Default.FetchContinueRequestResult,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch { /* session may be gone */ }
    }

    private void DecrementActiveAndCheckIdle()
    {
        var remaining = Interlocked.Decrement(ref _activeRequests);
        if (remaining <= 0)
        {
            lock (_idleLock)
                _idleTcs?.TrySetResult();
        }
    }

    private IFrame ResolveFrame(string? frameId)
    {
        if (frameId is not null && _page.TryGetFrame(frameId, out var frame) && frame is not null)
            return frame;
        return _page.GetFrameForSelectors();
    }

    private void NotifyRequestWaiters(MotusRequest request)
    {
        lock (_waiterLock)
        {
            for (int i = _requestWaiters.Count - 1; i >= 0; i--)
            {
                var (pattern, tcs) = _requestWaiters[i];
                if (Page.UrlMatchesStatic(request.Url, pattern))
                {
                    tcs.TrySetResult(request);
                    _requestWaiters.RemoveAt(i);
                }
            }
        }
    }

    private void NotifyResponseWaiters(MotusResponse response)
    {
        lock (_waiterLock)
        {
            for (int i = _responseWaiters.Count - 1; i >= 0; i--)
            {
                var (pattern, tcs) = _responseWaiters[i];
                if (Page.UrlMatchesStatic(response.Url, pattern))
                {
                    tcs.TrySetResult(response);
                    _responseWaiters.RemoveAt(i);
                }
            }
        }
    }
}
