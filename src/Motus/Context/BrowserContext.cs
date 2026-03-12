using System.Collections.Concurrent;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents an isolated browser context with its own cookies, cache, and storage.
/// </summary>
internal sealed class BrowserContext : IBrowserContext
{
    private readonly Browser _browser;
    private readonly CdpSessionRegistry _registry;
    private readonly string _browserContextId;
    private readonly List<Page> _pages = [];
    private readonly ConcurrentDictionary<string, Func<object?[], Task<object?>>> _bindings = new();
    private readonly List<string> _initScripts = [];
    private readonly LifecycleHookCollection _lifecycleHooks = new();
    private readonly Dictionary<string, Abstractions.IWaitCondition> _waitConditions = new();
    private readonly SelectorStrategyRegistry _selectorStrategies = new();
    private bool _closed;

    internal BrowserContext(Browser browser, CdpSessionRegistry registry, string browserContextId)
    {
        _browser = browser;
        _registry = registry;
        _browserContextId = browserContextId;

        _selectorStrategies.Register(new CssSelectorStrategy());
        _selectorStrategies.Register(new XPathSelectorStrategy());
        _selectorStrategies.Register(new TextSelectorStrategy());
        _selectorStrategies.Register(new RoleSelectorStrategy());
        _selectorStrategies.Register(new TestIdSelectorStrategy());
    }

    public IBrowser Browser => _browser;

    public IReadOnlyList<IPage> Pages
    {
        get
        {
            lock (_pages)
                return _pages.ToList();
        }
    }

    public ITracing Tracing
        => throw new NotImplementedException("Tracing is not yet implemented.");

    internal string BrowserContextId => _browserContextId;

    internal LifecycleHookCollection LifecycleHooks => _lifecycleHooks;

    internal SelectorStrategyRegistry SelectorStrategies => _selectorStrategies;

    internal void RegisterWaitCondition(string name, Abstractions.IWaitCondition condition)
    {
        lock (_waitConditions)
            _waitConditions[name] = condition;
    }

    internal Abstractions.IWaitCondition? GetWaitCondition(string name)
    {
        lock (_waitConditions)
            return _waitConditions.TryGetValue(name, out var c) ? c : null;
    }

    internal Abstractions.IPluginContext GetPluginContext() => new PluginContext(this);

    internal IReadOnlyDictionary<string, Func<object?[], Task<object?>>> Bindings => _bindings;

    internal IReadOnlyList<string> InitScripts
    {
        get
        {
            lock (_initScripts)
                return _initScripts.ToList();
        }
    }

    // --- Events ---
    public event EventHandler<IPage>? Page;
    public event EventHandler? Close;

    public async Task<IPage> NewPageAsync()
    {
        // Create a target in this browser context
        var createResult = await _registry.BrowserSession.SendAsync(
            "Target.createTarget",
            new TargetCreateTargetParams("about:blank", BrowserContextId: _browserContextId),
            CdpJsonContext.Default.TargetCreateTargetParams,
            CdpJsonContext.Default.TargetCreateTargetResult,
            CancellationToken.None);

        // Attach to the target to get a session
        var attachResult = await _registry.BrowserSession.SendAsync(
            "Target.attachToTarget",
            new TargetAttachToTargetParams(createResult.TargetId, Flatten: true),
            CdpJsonContext.Default.TargetAttachToTargetParams,
            CdpJsonContext.Default.TargetAttachToTargetResult,
            CancellationToken.None);

        var session = _registry.CreateSession(attachResult.SessionId);
        var page = new Page(session, this, createResult.TargetId);
        await page.InitializeAsync(CancellationToken.None);

        lock (_pages)
            _pages.Add(page);

        await _lifecycleHooks.FireOnPageCreatedAsync(page);
        Page?.Invoke(this, page);

        return page;
    }

    internal async Task ClosePageAsync(Page page, string targetId)
    {
        lock (_pages)
            _pages.Remove(page);

        try
        {
            await _registry.BrowserSession.SendAsync(
                "Target.closeTarget",
                new TargetCloseTargetParams(targetId),
                CdpJsonContext.Default.TargetCloseTargetParams,
                CdpJsonContext.Default.TargetCloseTargetResult,
                CancellationToken.None);
        }
        catch (CdpDisconnectedException)
        {
            // Target already gone
        }
    }

    public async Task CloseAsync()
    {
        if (_closed)
            return;

        _closed = true;

        // Close all pages
        List<Page> pagesToClose;
        lock (_pages)
            pagesToClose = _pages.ToList();

        foreach (var page in pagesToClose)
        {
            await _lifecycleHooks.FireOnPageClosedAsync(page);
            await page.DisposeAsync();
        }

        lock (_pages)
            _pages.Clear();

        // Dispose the browser context
        try
        {
            await _registry.BrowserSession.SendAsync(
                "Target.disposeBrowserContext",
                new TargetDisposeBrowserContextParams(_browserContextId),
                CdpJsonContext.Default.TargetDisposeBrowserContextParams,
                CdpJsonContext.Default.TargetDisposeBrowserContextResult,
                CancellationToken.None);
        }
        catch (CdpDisconnectedException)
        {
            // Browser already closed
        }

        Close?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync() => await CloseAsync();

    // --- Cookies ---

    public async Task<IReadOnlyList<Cookie>> CookiesAsync(IEnumerable<string>? urls = null)
    {
        var page = GetFirstPageOrThrow();
        var result = await page.Session.SendAsync(
            "Network.getCookies",
            new NetworkGetCookiesParams(urls?.ToArray()),
            CdpJsonContext.Default.NetworkGetCookiesParams,
            CdpJsonContext.Default.NetworkGetCookiesResult,
            CancellationToken.None);

        return result.Cookies.Select(c => new Cookie(
            c.Name, c.Value, c.Domain, c.Path, c.Expires,
            c.HttpOnly, c.Secure, ParseSameSite(c.SameSite))).ToList();
    }

    public async Task AddCookiesAsync(IEnumerable<Cookie> cookies)
    {
        var page = GetFirstPageOrThrow();
        foreach (var cookie in cookies)
        {
            await page.Session.SendAsync(
                "Network.setCookie",
                new NetworkSetCookieParams(
                    cookie.Name, cookie.Value,
                    Domain: cookie.Domain,
                    Path: cookie.Path,
                    Secure: cookie.Secure,
                    HttpOnly: cookie.HttpOnly,
                    SameSite: cookie.SameSite.ToString(),
                    Expires: cookie.Expires > 0 ? cookie.Expires : null),
                CdpJsonContext.Default.NetworkSetCookieParams,
                CdpJsonContext.Default.NetworkSetCookieResult,
                CancellationToken.None);
        }
    }

    public async Task ClearCookiesAsync()
    {
        var page = GetFirstPageOrThrow();
        await page.Session.SendAsync(
            "Network.clearBrowserCookies",
            CdpJsonContext.Default.NetworkClearBrowserCookiesResult,
            CancellationToken.None);
    }

    // --- Permissions ---

    public async Task GrantPermissionsAsync(IEnumerable<string> permissions, string? origin = null)
    {
        await _registry.BrowserSession.SendAsync(
            "Browser.grantPermissions",
            new BrowserGrantPermissionsParams(
                permissions.ToArray(),
                Origin: origin,
                BrowserContextId: _browserContextId),
            CdpJsonContext.Default.BrowserGrantPermissionsParams,
            CdpJsonContext.Default.BrowserGrantPermissionsResult,
            CancellationToken.None);
    }

    public async Task ClearPermissionsAsync()
    {
        await _registry.BrowserSession.SendAsync(
            "Browser.resetPermissions",
            CdpJsonContext.Default.BrowserResetPermissionsResult,
            CancellationToken.None);
    }

    // --- Geolocation ---

    public async Task SetGeolocationAsync(Geolocation? geolocation)
    {
        var page = GetFirstPageOrThrow();
        await page.Session.SendAsync(
            "Emulation.setGeolocationOverride",
            new EmulationSetGeolocationOverrideParams(
                geolocation?.Latitude,
                geolocation?.Longitude,
                geolocation?.Accuracy),
            CdpJsonContext.Default.EmulationSetGeolocationOverrideParams,
            CdpJsonContext.Default.EmulationSetGeolocationOverrideResult,
            CancellationToken.None);
    }

    // --- Bindings & Init Scripts ---

    public async Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback)
    {
        _bindings[name] = callback;

        // Apply to all existing pages
        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        foreach (var page in currentPages)
        {
            await page.ExposeBindingInternalAsync(name, callback);
        }
    }

    public async Task AddInitScriptAsync(string script)
    {
        lock (_initScripts)
            _initScripts.Add(script);

        // Apply to all existing pages
        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        foreach (var page in currentPages)
        {
            await page.AddInitScriptInternalAsync(script);
        }
    }

    // --- Stubbed methods ---

    public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler)
        => throw new NotImplementedException("Routing is not yet implemented (Phase 1J).");

    public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null)
        => throw new NotImplementedException("Routing is not yet implemented (Phase 1J).");

    public Task SetOfflineAsync(bool offline)
        => throw new NotImplementedException("SetOffline is not yet implemented (Phase 1J).");

    public Task SetExtraHTTPHeadersAsync(IDictionary<string, string> headers)
        => throw new NotImplementedException("SetExtraHTTPHeaders is not yet implemented (Phase 1J).");

    public Task<StorageState> StorageStateAsync(string? path = null)
        => throw new NotImplementedException("StorageState is not yet implemented.");

    // --- Helpers ---

    private Page GetFirstPageOrThrow()
    {
        lock (_pages)
        {
            if (_pages.Count == 0)
                throw new InvalidOperationException(
                    "This operation requires at least one page in the context.");
            return _pages[0];
        }
    }

    private static SameSiteAttribute ParseSameSite(string sameSite) =>
        sameSite switch
        {
            "Strict" => SameSiteAttribute.Strict,
            "Lax" => SameSiteAttribute.Lax,
            "None" => SameSiteAttribute.None,
            _ => SameSiteAttribute.Lax
        };
}
