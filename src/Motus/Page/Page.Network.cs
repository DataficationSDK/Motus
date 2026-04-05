using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    private NetworkManager? _networkManager;

    private readonly List<(string Pattern, Func<IRoute, Task> Handler)> _pageRoutes = [];
    private readonly object _routeLock = new();

    internal async Task InitializeNetworkAsync(CancellationToken ct)
    {
        _networkManager = new NetworkManager(_session, this, _pageCts.Token);
        bool hasFetchRoutes = HasAnyRoutes();
        await _networkManager.InitializeAsync(hasFetchRoutes, ct).ConfigureAwait(false);
    }

    internal async Task EnableAuthHandlingAsync()
    {
        if (_networkManager is not null
            && (_session.Capabilities & MotusCapabilities.FetchInterception) != 0)
            await _networkManager.EnableFetchWithAuthAsync(_pageCts.Token).ConfigureAwait(false);
    }

    private bool HasAnyRoutes()
    {
        lock (_routeLock)
            return _pageRoutes.Count > 0 || _context.HasAnyRoutes();
    }

    public async Task RouteAsync(string urlPattern, Func<IRoute, Task> handler)
    {
        bool wasEmpty;
        lock (_routeLock)
        {
            wasEmpty = !HasAnyRoutes();
            _pageRoutes.Add((urlPattern, handler));
        }

        if (wasEmpty && _networkManager is not null)
            await _networkManager.EnableFetchAsync(_pageCts.Token).ConfigureAwait(false);
    }

    public async Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null)
    {
        lock (_routeLock)
        {
            if (handler is null)
                _pageRoutes.RemoveAll(r => r.Pattern == urlPattern);
            else
                _pageRoutes.RemoveAll(r => r.Pattern == urlPattern && r.Handler == handler);
        }

        if (!HasAnyRoutes() && _networkManager is not null)
            await _networkManager.DisableFetchAsync(_pageCts.Token).ConfigureAwait(false);
    }

    public Task<IRequest> WaitForRequestAsync(string urlPattern, double? timeout = null)
    {
        if (_networkManager is null)
            throw new InvalidOperationException("NetworkManager is not initialized.");
        return _networkManager.WaitForRequestAsync(
            urlPattern, TimeSpan.FromMilliseconds(timeout ?? 30_000));
    }

    public Task<IResponse> WaitForResponseAsync(string urlPattern, double? timeout = null)
    {
        if (_networkManager is null)
            throw new InvalidOperationException("NetworkManager is not initialized.");
        return _networkManager.WaitForResponseAsync(
            urlPattern, TimeSpan.FromMilliseconds(timeout ?? 30_000));
    }

    internal (string? Pattern, Func<IRoute, Task>? Handler) FindRouteHandler(string url)
    {
        lock (_routeLock)
        {
            for (int i = _pageRoutes.Count - 1; i >= 0; i--)
            {
                var (p, h) = _pageRoutes[i];
                if (UrlMatchesStatic(url, p))
                    return (p, h);
            }
        }

        return _context.FindRouteHandler(url);
    }

    internal void FireRequest(IRequest req) =>
        Request?.Invoke(this, new RequestEventArgs(req));

    internal void FireResponse(IResponse res) =>
        Response?.Invoke(this, new ResponseEventArgs(res));

    internal void FireRequestFinished(IRequest req) =>
        RequestFinished?.Invoke(this, new RequestEventArgs(req));

    internal void FireRequestFailed(IRequest req) =>
        RequestFailed?.Invoke(this, new RequestEventArgs(req));
}
