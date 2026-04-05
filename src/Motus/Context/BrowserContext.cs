using System.Collections.Concurrent;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents an isolated browser context with its own cookies, cache, and storage.
/// </summary>
internal sealed class BrowserContext : IBrowserContext
{
    /// <summary>
    /// Global static hook invoked whenever any page is created in any context.
    /// Used by the visual runner to detect test-created pages.
    /// </summary>
    internal static Action<Abstractions.IPage>? GlobalPageCreated;

    private readonly Browser _browser;
    private readonly IMotusSessionRegistry _registry;
    private readonly string _browserContextId;
    private readonly List<Page> _pages = [];
    private readonly ConcurrentDictionary<string, Func<object?[], Task<object?>>> _bindings = new();
    private readonly List<string> _initScripts = [];
    private readonly LifecycleHookCollection _lifecycleHooks = new();
    private readonly ReporterCollection _reporters = new();
    private readonly Dictionary<string, Abstractions.IWaitCondition> _waitConditions = new();
    private readonly SelectorStrategyRegistry _selectorStrategies = new();
    private readonly AccessibilityRuleCollection _accessibilityRules = new();
    private readonly List<(string Pattern, Func<IRoute, Task> Handler)> _contextRoutes = [];
    private readonly object _contextRouteLock = new();
    private readonly Dictionary<string, string> _extraHeaders = new();
    private readonly ContextOptions? _options;
    private volatile bool _offline;
    private int _closed;
    private int _storageStateRestored;

    private readonly Tracing _tracing;

    internal BrowserContext(Browser browser, IMotusSessionRegistry registry, string browserContextId, ContextOptions? options = null)
    {
        _browser = browser;
        _registry = registry;
        _browserContextId = browserContextId;
        _options = options;
        _tracing = new Tracing(registry.BrowserSession);

        if (_options?.ExtraHttpHeaders is not null)
        {
            foreach (var kv in _options.ExtraHttpHeaders)
                _extraHeaders[kv.Key] = kv.Value;
        }

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

    public ITracing Tracing => _tracing;

    internal string BrowserContextId => _browserContextId;

    internal ContextOptions? Options => _options;

    internal string? BaseURL => _options?.BaseURL;

    internal LifecycleHookCollection LifecycleHooks => _lifecycleHooks;

    internal ReporterCollection Reporters => _reporters;

    internal SelectorStrategyRegistry SelectorStrategies => _selectorStrategies;

    internal AccessibilityRuleCollection AccessibilityRules => _accessibilityRules;

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

    internal PluginHost? PluginHost { get; set; }

    public Abstractions.IPluginContext GetPluginContext() => new PluginContext(this);

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
            CancellationToken.None).ConfigureAwait(false);

        // Attach to the target to get a session
        var attachResult = await _registry.BrowserSession.SendAsync(
            "Target.attachToTarget",
            new TargetAttachToTargetParams(createResult.TargetId, Flatten: true),
            CdpJsonContext.Default.TargetAttachToTargetParams,
            CdpJsonContext.Default.TargetAttachToTargetResult,
            CancellationToken.None).ConfigureAwait(false);

        var session = _registry.CreateSession(attachResult.SessionId);
        var page = new Page(session, this, createResult.TargetId);
        await page.InitializeAsync(CancellationToken.None).ConfigureAwait(false);

        // Apply context options (viewport, locale, timezone, etc.)
        await ApplyContextOptionsToPageAsync(page).ConfigureAwait(false);

        // Propagate context-level extra headers to the new page
        Dictionary<string, string> extraHeaders;
        lock (_extraHeaders)
            extraHeaders = new Dictionary<string, string>(_extraHeaders);
        if (extraHeaders.Count > 0)
        {
            await page.Session.SendAsync(
                "Network.setExtraHTTPHeaders",
                new NetworkSetExtraHttpHeadersParams(extraHeaders),
                CdpJsonContext.Default.NetworkSetExtraHttpHeadersParams,
                CdpJsonContext.Default.NetworkSetExtraHttpHeadersResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        // Propagate context-level offline state to the new page
        if (_offline)
        {
            await page.Session.SendAsync(
                "Network.emulateNetworkConditions",
                new NetworkEmulateNetworkConditionsParams(
                    Offline: true, Latency: 0,
                    DownloadThroughput: -1, UploadThroughput: -1),
                CdpJsonContext.Default.NetworkEmulateNetworkConditionsParams,
                CdpJsonContext.Default.NetworkEmulateNetworkConditionsResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        // Restore storage state (one-time per context)
        if (_options?.StorageState is not null && Interlocked.CompareExchange(ref _storageStateRestored, 1, 0) == 0)
        {
            var state = _options.StorageState;

            if (state.Cookies.Count > 0)
                await AddCookiesInternalAsync(page, state.Cookies).ConfigureAwait(false);

            if (state.Origins.Count > 0)
            {
                foreach (var origin in state.Origins)
                {
                    if (origin.LocalStorage.Count == 0)
                        continue;

                    var script = string.Join("\n", origin.LocalStorage.Select(kv =>
                        $"localStorage.setItem({System.Text.Json.JsonSerializer.Serialize(kv.Key)}, {System.Text.Json.JsonSerializer.Serialize(kv.Value)});"));

                    await page.Session.SendAsync(
                        "Runtime.evaluate",
                        new RuntimeEvaluateParams(Expression: script, ReturnByValue: true),
                        CdpJsonContext.Default.RuntimeEvaluateParams,
                        CdpJsonContext.Default.RuntimeEvaluateResult,
                        CancellationToken.None).ConfigureAwait(false);
                }
            }
        }

        // Start video recording if configured
        if (_options?.RecordVideo is { } videoOpts)
        {
            var size = videoOpts.Size ?? DeriveVideoSize(_options.Viewport);
            var path = Path.Combine(videoOpts.Dir, $"video-{Guid.NewGuid():N}.avi");
            var recorder = new VideoRecorder(page, path, size.Width, size.Height);
            await recorder.StartAsync(CancellationToken.None).ConfigureAwait(false);
            page.SetVideoRecorder(recorder);
        }

        lock (_pages)
            _pages.Add(page);

        await _lifecycleHooks.FireOnPageCreatedAsync(page).ConfigureAwait(false);
        Page?.Invoke(this, page);
        GlobalPageCreated?.Invoke(page);

        return page;
    }

    private static ViewportSize DeriveVideoSize(ViewportSize? viewport)
    {
        if (viewport is null)
            return new ViewportSize(800, 600);

        // Scale down to fit within 800x800
        var scale = Math.Min(800.0 / viewport.Width, 800.0 / viewport.Height);
        if (scale >= 1.0)
            return viewport;

        return new ViewportSize((int)(viewport.Width * scale), (int)(viewport.Height * scale));
    }

    internal async Task ClosePageAsync(Page page, string targetId)
    {
        var sessionId = page.Session.SessionId;

        lock (_pages)
            _pages.Remove(page);

        try
        {
            await _registry.BrowserSession.SendAsync(
                "Target.closeTarget",
                new TargetCloseTargetParams(targetId),
                CdpJsonContext.Default.TargetCloseTargetParams,
                CdpJsonContext.Default.TargetCloseTargetResult,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (CdpDisconnectedException)
        {
            // Target already gone
        }

        // Clean up CDP session and event channels
        if (sessionId is not null)
        {
            _registry.RemoveSession(sessionId);
            page.Session.CleanupChannels();
        }
    }

    public async Task CloseAsync()
    {
        if (Interlocked.CompareExchange(ref _closed, 1, 0) != 0)
            return;

        // Unload plugins before closing pages
        if (PluginHost is not null)
            await PluginHost.UnloadAsync().ConfigureAwait(false);

        // Close all pages
        List<Page> pagesToClose;
        lock (_pages)
            pagesToClose = _pages.ToList();

        foreach (var page in pagesToClose)
        {
            await _lifecycleHooks.FireOnPageClosedAsync(page).ConfigureAwait(false);
            var sessionId = page.Session.SessionId;
            await page.DisposeAsync().ConfigureAwait(false);

            // Clean up session and event channels to prevent resource accumulation
            if (sessionId is not null)
            {
                _registry.RemoveSession(sessionId);
                page.Session.CleanupChannels();
            }
        }

        lock (_pages)
            _pages.Clear();

        _browser.RemoveContext(this);

        // Dispose the browser context
        try
        {
            await _registry.BrowserSession.SendAsync(
                "Target.disposeBrowserContext",
                new TargetDisposeBrowserContextParams(_browserContextId),
                CdpJsonContext.Default.TargetDisposeBrowserContextParams,
                CdpJsonContext.Default.TargetDisposeBrowserContextResult,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (CdpDisconnectedException)
        {
            // Browser already closed
        }

        Close?.Invoke(this, EventArgs.Empty);
    }

    public async ValueTask DisposeAsync() => await CloseAsync().ConfigureAwait(false);

    // --- Cookies ---

    public async Task<IReadOnlyList<Cookie>> CookiesAsync(IEnumerable<string>? urls = null)
    {
        var page = GetFirstPageOrThrow();
        var result = await page.Session.SendAsync(
            "Network.getCookies",
            new NetworkGetCookiesParams(urls?.ToArray()),
            CdpJsonContext.Default.NetworkGetCookiesParams,
            CdpJsonContext.Default.NetworkGetCookiesResult,
            CancellationToken.None).ConfigureAwait(false);

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
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task ClearCookiesAsync()
    {
        var page = GetFirstPageOrThrow();
        await page.Session.SendAsync(
            "Network.clearBrowserCookies",
            CdpJsonContext.Default.NetworkClearBrowserCookiesResult,
            CancellationToken.None).ConfigureAwait(false);
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
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task ClearPermissionsAsync()
    {
        await _registry.BrowserSession.SendAsync(
            "Browser.resetPermissions",
            CdpJsonContext.Default.BrowserResetPermissionsResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    // --- Geolocation ---

    public async Task SetGeolocationAsync(Geolocation? geolocation)
    {
        var page = GetFirstPageOrThrow();
        CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
            "Geolocation override", CapabilityGuard.GetTransportDescription(page.Session));
        await page.Session.SendAsync(
            "Emulation.setGeolocationOverride",
            new EmulationSetGeolocationOverrideParams(
                geolocation?.Latitude,
                geolocation?.Longitude,
                geolocation?.Accuracy),
            CdpJsonContext.Default.EmulationSetGeolocationOverrideParams,
            CdpJsonContext.Default.EmulationSetGeolocationOverrideResult,
            CancellationToken.None).ConfigureAwait(false);
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
            await page.ExposeBindingInternalAsync(name, callback).ConfigureAwait(false);
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
            await page.AddInitScriptInternalAsync(script).ConfigureAwait(false);
        }
    }

    // --- Network routing ---

    public async Task RouteAsync(string urlPattern, Func<IRoute, Task> handler)
    {
        lock (_contextRouteLock)
            _contextRoutes.Add((urlPattern, handler));

        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        foreach (var page in currentPages)
            await page.RouteAsync(urlPattern, handler).ConfigureAwait(false);
    }

    public async Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null)
    {
        lock (_contextRouteLock)
        {
            if (handler is null)
                _contextRoutes.RemoveAll(r => r.Pattern == urlPattern);
            else
                _contextRoutes.RemoveAll(r => r.Pattern == urlPattern && r.Handler == handler);
        }

        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        foreach (var page in currentPages)
            await page.UnrouteAsync(urlPattern, handler).ConfigureAwait(false);
    }

    public async Task SetOfflineAsync(bool offline)
    {
        _offline = offline;

        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        foreach (var page in currentPages)
        {
            await page.Session.SendAsync(
                "Network.emulateNetworkConditions",
                new NetworkEmulateNetworkConditionsParams(
                    Offline: offline, Latency: 0,
                    DownloadThroughput: -1, UploadThroughput: -1),
                CdpJsonContext.Default.NetworkEmulateNetworkConditionsParams,
                CdpJsonContext.Default.NetworkEmulateNetworkConditionsResult,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async Task SetExtraHTTPHeadersAsync(IDictionary<string, string> headers)
    {
        lock (_extraHeaders)
        {
            _extraHeaders.Clear();
            foreach (var kv in headers)
                _extraHeaders[kv.Key] = kv.Value;
        }

        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        foreach (var page in currentPages)
        {
            await page.Session.SendAsync(
                "Network.setExtraHTTPHeaders",
                new NetworkSetExtraHttpHeadersParams(new Dictionary<string, string>(headers)),
                CdpJsonContext.Default.NetworkSetExtraHttpHeadersParams,
                CdpJsonContext.Default.NetworkSetExtraHttpHeadersResult,
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    internal (string? Pattern, Func<IRoute, Task>? Handler) FindRouteHandler(string url)
    {
        lock (_contextRouteLock)
        {
            for (int i = _contextRoutes.Count - 1; i >= 0; i--)
            {
                var (p, h) = _contextRoutes[i];
                if (Motus.Page.UrlMatchesStatic(url, p))
                    return (p, h);
            }
        }
        return (null, null);
    }

    internal bool HasAnyRoutes()
    {
        lock (_contextRouteLock)
            return _contextRoutes.Count > 0;
    }

    public async Task<StorageState> StorageStateAsync(string? path = null)
    {
        IReadOnlyList<Cookie> cookies;
        List<Page> currentPages;
        lock (_pages)
            currentPages = _pages.ToList();

        if (currentPages.Count > 0)
        {
            cookies = await CookiesAsync().ConfigureAwait(false);
        }
        else
        {
            cookies = [];
        }

        var origins = new List<OriginStorage>();
        foreach (var page in currentPages)
        {
            var url = page.Url;
            if (string.IsNullOrEmpty(url) || url == "about:blank")
                continue;

            try
            {
                var origin = new Uri(url).GetLeftPart(UriPartial.Authority);
                var result = await page.Session.SendAsync(
                    "Runtime.evaluate",
                    new RuntimeEvaluateParams(
                        Expression: "JSON.stringify(Object.fromEntries(Object.entries(localStorage)))",
                        ReturnByValue: true),
                    CdpJsonContext.Default.RuntimeEvaluateParams,
                    CdpJsonContext.Default.RuntimeEvaluateResult,
                    CancellationToken.None).ConfigureAwait(false);

                if (result.Result.Value is System.Text.Json.JsonElement el
                    && el.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var json = el.GetString();
                    if (!string.IsNullOrEmpty(json))
                    {
                        var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                        if (dict is { Count: > 0 })
                        {
                            origins.Add(new OriginStorage(origin, dict.ToList()));
                        }
                    }
                }
            }
            catch
            {
                // Skip pages where localStorage is inaccessible
            }
        }

        var state = new StorageState(cookies, origins);

        if (path is not null)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(state);
            await File.WriteAllTextAsync(path, json).ConfigureAwait(false);
        }

        return state;
    }

    // --- Helpers ---

    private async Task ApplyContextOptionsToPageAsync(Page page)
    {
        if (_options is null)
            return;

        var desc = CapabilityGuard.GetTransportDescription(page.Session);

        if (_options.Viewport is { } viewport)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
                "Viewport emulation", desc);
            page.SetViewportInternal(viewport);
            await page.Session.SendAsync(
                "Emulation.setDeviceMetricsOverride",
                new EmulationSetDeviceMetricsOverrideParams(
                    Width: viewport.Width,
                    Height: viewport.Height,
                    DeviceScaleFactor: 1,
                    Mobile: false),
                CdpJsonContext.Default.EmulationSetDeviceMetricsOverrideParams,
                CdpJsonContext.Default.EmulationSetDeviceMetricsOverrideResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.Locale is not null)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
                "Locale override", desc);
            await page.Session.SendAsync(
                "Emulation.setLocaleOverride",
                new EmulationSetLocaleOverrideParams(_options.Locale),
                CdpJsonContext.Default.EmulationSetLocaleOverrideParams,
                CdpJsonContext.Default.EmulationSetLocaleOverrideResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.TimezoneId is not null)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
                "Timezone override", desc);
            await page.Session.SendAsync(
                "Emulation.setTimezoneOverride",
                new EmulationSetTimezoneOverrideParams(_options.TimezoneId),
                CdpJsonContext.Default.EmulationSetTimezoneOverrideParams,
                CdpJsonContext.Default.EmulationSetTimezoneOverrideResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.ColorScheme is not null)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
                "Color scheme emulation", desc);
            await page.Session.SendAsync(
                "Emulation.setEmulatedMedia",
                new EmulationSetEmulatedMediaParams(
                    Features: [new EmulationMediaFeature(
                        "prefers-color-scheme",
                        _options.ColorScheme.Value.ToString().ToLowerInvariant())]),
                CdpJsonContext.Default.EmulationSetEmulatedMediaParams,
                CdpJsonContext.Default.EmulationSetEmulatedMediaResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.UserAgent is not null)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
                "User-agent override", desc);
            await page.Session.SendAsync(
                "Emulation.setUserAgentOverride",
                new EmulationSetUserAgentOverrideParams(_options.UserAgent),
                CdpJsonContext.Default.EmulationSetUserAgentOverrideParams,
                CdpJsonContext.Default.EmulationSetUserAgentOverrideResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.IgnoreHTTPSErrors)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.SecurityOverrides,
                "HTTPS error override", desc);
            await page.Session.SendAsync(
                "Security.enable",
                CdpJsonContext.Default.SecurityEnableResult,
                CancellationToken.None).ConfigureAwait(false);

            await page.Session.SendAsync(
                "Security.setIgnoreCertificateErrors",
                new SecuritySetIgnoreCertificateErrorsParams(Ignore: true),
                CdpJsonContext.Default.SecuritySetIgnoreCertificateErrorsParams,
                CdpJsonContext.Default.SecuritySetIgnoreCertificateErrorsResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.Geolocation is not null)
        {
            CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.EmulationOverrides,
                "Geolocation override", desc);
            await page.Session.SendAsync(
                "Emulation.setGeolocationOverride",
                new EmulationSetGeolocationOverrideParams(
                    _options.Geolocation.Latitude,
                    _options.Geolocation.Longitude,
                    _options.Geolocation.Accuracy),
                CdpJsonContext.Default.EmulationSetGeolocationOverrideParams,
                CdpJsonContext.Default.EmulationSetGeolocationOverrideResult,
                CancellationToken.None).ConfigureAwait(false);
        }

        if (_options.HttpCredentials is not null)
        {
            await page.EnableAuthHandlingAsync().ConfigureAwait(false);
        }
    }

    private async Task AddCookiesInternalAsync(Page page, IReadOnlyList<Cookie> cookies)
    {
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
                CancellationToken.None).ConfigureAwait(false);
        }
    }

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
