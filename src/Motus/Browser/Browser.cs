using System.Diagnostics;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Manages a Chromium browser instance connected via CDP.
/// </summary>
internal sealed class Browser : IBrowser
{
    private readonly IMotusTransport _transport;
    private readonly IMotusSessionRegistry _registry;
    private readonly Process? _process;
    private readonly string? _tempUserDataDir;
    private readonly bool _handleSigint;
    private readonly bool _handleSigterm;
    private readonly LaunchOptions _launchOptions;
    private readonly BrowserOutputDrain? _output;

    // Motus is responsible for ending only a browser it started. Everything that terminates the
    // browser reads this rather than testing the process field, so the two can never disagree.
    private readonly bool _ownsProcess;
    private readonly bool _adoptExistingTargets;

    private readonly List<BrowserContext> _contexts = [];
    private readonly CancellationTokenSource _browserCts = new();

    private volatile bool _isConnected;
    private int _disconnectedFlag;
    private int _closedFlag;
    private BrowserHeartbeat? _heartbeat;
    private ConsoleCancelEventHandler? _cancelHandler;
    private EventHandler? _processExitHandler;

    internal Browser(
        IMotusTransport transport,
        IMotusSessionRegistry registry,
        Process? process,
        string? tempUserDataDir,
        bool handleSigint,
        bool handleSigterm,
        LaunchOptions? launchOptions = null,
        BrowserOutputDrain? output = null,
        bool adoptExistingTargets = false)
    {
        _transport = transport;
        _registry = registry;
        _process = process;
        _tempUserDataDir = tempUserDataDir;
        _handleSigint = handleSigint;
        _handleSigterm = handleSigterm;
        _launchOptions = launchOptions ?? new LaunchOptions();
        _output = output;
        _ownsProcess = process is not null;
        _adoptExistingTargets = adoptExistingTargets;

        _transport.Disconnected += OnTransportDisconnected;

        if (_ownsProcess)
        {
            _process!.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;
        }
    }

    public bool IsConnected => _isConnected;

    public bool IsHealthy => _isConnected && (_process is null || !_process.HasExited);

    public bool OwnsProcess => _ownsProcess;

    public string Version { get; private set; } = string.Empty;

    public IReadOnlyList<IBrowserContext> Contexts
    {
        get
        {
            lock (_contexts)
                return _contexts.ToList();
        }
    }

    public event EventHandler? Disconnected;

    internal async Task InitializeAsync(CancellationToken ct)
    {
        var response = await _registry.BrowserSession.SendAsync(
            "Browser.getVersion",
            CdpJsonContext.Default.BrowserGetVersionResult,
            ct).ConfigureAwait(false);

        Version = response.Product;
        _isConnected = true;

        RegisterSignalHandlers();

        if (_ownsProcess)
        {
            _heartbeat = new BrowserHeartbeat(_registry.BrowserSession, OnHeartbeatFailed);
            _heartbeat.Start();
        }

        await AdoptExistingTargetsAsync(ct).ConfigureAwait(false);
    }

    public async Task CloseAsync()
    {
        // Closing twice is not a fault. A signal handler and an explicit close can both arrive, and
        // the second must not cut short what the first is doing.
        if (Interlocked.CompareExchange(ref _closedFlag, 1, 0) != 0)
            return;

        if (!_isConnected)
        {
            // The browser was already given up on, by a lost connection or by a heartbeat that
            // stopped being answered. Neither of those ends the process: a frozen browser does not
            // leave on its own, and one left behind holds its profile directory and its port.
            UnregisterSignalHandlers();
            UnregisterProcessExitHandler();
            _browserCts.Cancel();
            await EndProcessAsync().ConfigureAwait(false);
            return;
        }

        _isConnected = false;

        if (_heartbeat is not null)
            await _heartbeat.DisposeAsync().ConfigureAwait(false);

        UnregisterSignalHandlers();
        UnregisterProcessExitHandler();
        _browserCts.Cancel();

        // Close all contexts. An adopted one only lets go of its pages and unloads its plugins;
        // it does not dispose the browser context, so windows belonging to whoever is using the
        // browser survive. That guard lives on the context itself.
        List<BrowserContext> contextsToClose;
        lock (_contexts)
            contextsToClose = _contexts.ToList();

        foreach (var context in contextsToClose)
        {
            await context.CloseAsync().ConfigureAwait(false);
        }

        lock (_contexts)
            _contexts.Clear();

        // A browser Motus did not start is not Motus's to end. Closing such a browser means
        // letting go of it, which is what disconnecting already does.
        if (!_ownsProcess)
        {
            await _transport.DisposeAsync().ConfigureAwait(false);
            return;
        }

        try
        {
            await _registry.BrowserSession.SendAsync(
                "Browser.close",
                CdpJsonContext.Default.BrowserCloseResult,
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is CdpDisconnectedException or MotusTargetClosedException)
        {
            // Expected: browser closes the WebSocket on shutdown
        }

        // Dispose the transport to terminate the WebSocket receive loop
        await _transport.DisposeAsync().ConfigureAwait(false);

        await EndProcessAsync().ConfigureAwait(false);
    }

    public async Task DisconnectAsync()
    {
        // Claiming the close flag stops a later CloseAsync from doing anything, and keeps the
        // transport disposal below from being reported as an unexpected loss.
        if (Interlocked.CompareExchange(ref _closedFlag, 1, 0) != 0)
            return;

        _isConnected = false;

        if (_heartbeat is not null)
            await _heartbeat.DisposeAsync().ConfigureAwait(false);

        UnregisterSignalHandlers();
        UnregisterProcessExitHandler();
        _browserCts.Cancel();

        await _transport.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Gives the browser process a moment to leave on its own, then ends it.
    /// </summary>
    private async Task EndProcessAsync()
    {
        if (_process is null || _process.HasExited)
            return;

        using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try
        {
            await _process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // It left while we were waiting on it
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Disposal ends the process itself, and the Process object with it, so a close arriving
        // afterwards has nothing left to act on.
        Interlocked.Exchange(ref _closedFlag, 1);

        if (_heartbeat is not null)
            await _heartbeat.DisposeAsync().ConfigureAwait(false);

        UnregisterSignalHandlers();
        UnregisterProcessExitHandler();
        _browserCts.Cancel();

        _isConnected = false;

        await _transport.DisposeAsync().ConfigureAwait(false);

        if (_ownsProcess)
        {
            if (!_process!.HasExited)
            {
                try
                {
                    _process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // Process already exited
                }
            }

            _process.Dispose();
        }

        if (_tempUserDataDir is not null)
        {
            try
            {
                Directory.Delete(_tempUserDataDir, recursive: true);
            }
            catch
            {
                // Best-effort cleanup
            }
        }
    }

    public async Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
    {
        options = ConfigMerge.ApplyConfig(options ?? new ContextOptions());

        var result = await _registry.BrowserSession.SendAsync(
            "Target.createBrowserContext",
            new TargetCreateBrowserContextParams(
                DisposeOnDetach: true,
                ProxyServer: options?.Proxy?.Server),
            CdpJsonContext.Default.TargetCreateBrowserContextParams,
            CdpJsonContext.Default.TargetCreateBrowserContextResult,
            CancellationToken.None).ConfigureAwait(false);

        var context = new BrowserContext(this, _registry, result.BrowserContextId, options);

        var host = new PluginHost();
        await host.LoadAsync(_launchOptions, context).ConfigureAwait(false);
        context.PluginHost = host;

        lock (_contexts)
            _contexts.Add(context);

        if (options?.Permissions is { Count: > 0 })
            await context.GrantPermissionsAsync(options.Permissions).ConfigureAwait(false);

        return context;
    }

    public async Task<IPage> NewPageAsync(ContextOptions? options = null)
    {
        var context = await NewContextAsync(options).ConfigureAwait(false);
        return await context.NewPageAsync().ConfigureAwait(false);
    }

    internal void RemoveContext(BrowserContext context)
    {
        lock (_contexts)
            _contexts.Remove(context);
    }

    /// <summary>
    /// The CDP target type for a browser tab. Workers, service workers, and the browser target
    /// itself all come back from target discovery and are not pages.
    /// </summary>
    private const string PageTargetType = "page";

    /// <summary>
    /// Brings the contexts and pages already open in the browser under this handle, so a caller
    /// who connected to a running browser can see and drive what is in it.
    /// </summary>
    private async Task AdoptExistingTargetsAsync(CancellationToken ct)
    {
        if (!_adoptExistingTargets)
            return;

        // Adoption is expressed through target discovery, which only a transport that multiplexes
        // targets offers. One that models browsing contexts its own way has nothing to adopt here
        // and must not be pushed through these semantics.
        if ((_registry.BrowserSession.Capabilities & MotusCapabilities.TargetMultiplexing) == 0)
            return;

        await _registry.BrowserSession.SendAsync(
            "Target.setDiscoverTargets",
            new TargetSetDiscoverTargetsParams(Discover: true),
            CdpJsonContext.Default.TargetSetDiscoverTargetsParams,
            CdpJsonContext.Default.TargetSetDiscoverTargetsResult,
            ct).ConfigureAwait(false);

        var targets = await _registry.BrowserSession.SendAsync(
            "Target.getTargets",
            CdpJsonContext.Default.TargetGetTargetsResult,
            ct).ConfigureAwait(false);

        foreach (var info in targets.TargetInfos)
        {
            if (info.Type != PageTargetType)
                continue;

            var context = await GetOrCreateAdoptedContextAsync(info.BrowserContextId).ConfigureAwait(false);
            await AdoptPageTargetAsync(context, info.TargetId, ct).ConfigureAwait(false);
        }

        // Only now, so the pump cannot race the enumeration above over the same target.
        StartTargetLifecyclePump();
    }

    /// <summary>
    /// Returns the adopted context for a browser context id, creating one the first time that id
    /// is seen. Targets that report no browser context belong to the browser's default one.
    /// </summary>
    private async Task<BrowserContext> GetOrCreateAdoptedContextAsync(string? browserContextId)
    {
        var id = browserContextId ?? string.Empty;

        var existing = FindAdoptedContext(id);
        if (existing is not null)
            return existing;

        var context = new BrowserContext(this, _registry, id, options: null, adopted: true);

        // Selector strategies are registered by the built-in plugins and by nothing else, so a
        // context without a plugin host cannot resolve a single locator.
        var host = new PluginHost();
        await host.LoadAsync(_launchOptions, context).ConfigureAwait(false);
        context.PluginHost = host;

        lock (_contexts)
        {
            existing = _contexts.FirstOrDefault(c => c.IsAdopted && c.BrowserContextId == id);
            if (existing is null)
            {
                _contexts.Add(context);
                return context;
            }
        }

        await host.UnloadAsync().ConfigureAwait(false);
        return existing;
    }

    private BrowserContext? FindAdoptedContext(string id)
    {
        lock (_contexts)
            return _contexts.FirstOrDefault(c => c.IsAdopted && c.BrowserContextId == id);
    }

    /// <summary>
    /// Attaches to a page target that already exists and wraps it as a page on the given context.
    /// </summary>
    private async Task AdoptPageTargetAsync(BrowserContext context, string targetId, CancellationToken ct)
    {
        string? sessionId = null;
        Page? page = null;

        try
        {
            var attachResult = await _registry.BrowserSession.SendAsync(
                "Target.attachToTarget",
                new TargetAttachToTargetParams(targetId, Flatten: true),
                CdpJsonContext.Default.TargetAttachToTargetParams,
                CdpJsonContext.Default.TargetAttachToTargetResult,
                ct).ConfigureAwait(false);

            sessionId = attachResult.SessionId;
            var session = _registry.CreateSession(sessionId);
            page = new Page(session, context, targetId);

            await page.InitializeAsync(ct).ConfigureAwait(false);

            // A tab that was already open has already navigated, so enabling the Page domain
            // replays nothing and the frame map would stay empty. Read the tree instead.
            await page.SeedFrameTreeAsync(ct).ConfigureAwait(false);

            await context.AdoptPageAsync(page).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One page that will not answer is not a reason to fail the whole connection, or to
            // drop every other page in the browser on the floor.
            if (page is not null)
                await page.DisposeAsync().ConfigureAwait(false);

            if (sessionId is not null)
                _registry.RemoveSession(sessionId);

            Console.Error.WriteLine($"Motus: an open page could not be adopted ({ex.Message}).");
        }
    }

    /// <summary>
    /// Keeps the adopted view accurate while attached, so pages opened and closed by whoever else
    /// is using the browser appear and disappear rather than being a snapshot taken at connect.
    /// </summary>
    private void StartTargetLifecyclePump()
    {
        var ct = _browserCts.Token;

        _ = PumpTargetEventsAsync(
            "Target.targetCreated",
            CdpJsonContext.Default.TargetTargetCreatedEvent,
            OnTargetCreatedAsync, ct);

        _ = PumpTargetEventsAsync(
            "Target.targetDestroyed",
            CdpJsonContext.Default.TargetTargetDestroyedEvent,
            OnTargetDestroyedAsync, ct);
    }

    private async Task PumpTargetEventsAsync<T>(
        string eventName,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Func<T, Task> handler,
        CancellationToken ct)
    {
        try
        {
            await foreach (var evt in _registry.BrowserSession
                               .SubscribeAsync(eventName, typeInfo, ct).ConfigureAwait(false))
            {
                try
                {
                    await handler(evt).ConfigureAwait(false);
                }
                catch
                {
                    // One target that cannot be handled must not end the pump for the rest.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when the browser handle is released
        }
    }

    private async Task OnTargetCreatedAsync(TargetTargetCreatedEvent evt)
    {
        var info = evt.TargetInfo;
        if (info.Type != PageTargetType)
            return;

        var id = info.BrowserContextId ?? string.Empty;

        BrowserContext? context;
        lock (_contexts)
            context = _contexts.FirstOrDefault(c => c.BrowserContextId == id);

        // A page in a context Motus created is that context's own business: it builds and
        // registers the page itself, and adopting it here would make a second handle to one tab.
        if (context is not null && !context.IsAdopted)
            return;

        context ??= await GetOrCreateAdoptedContextAsync(id).ConfigureAwait(false);

        if (context.HasPageForTarget(info.TargetId))
            return;

        await AdoptPageTargetAsync(context, info.TargetId, _browserCts.Token).ConfigureAwait(false);
    }

    private async Task OnTargetDestroyedAsync(TargetTargetDestroyedEvent evt)
    {
        List<BrowserContext> adopted;
        lock (_contexts)
            adopted = _contexts.Where(c => c.IsAdopted).ToList();

        foreach (var context in adopted)
        {
            if (await context.RetirePageAsync(evt.TargetId).ConfigureAwait(false))
                return;
        }
    }

    private void OnTransportDisconnected(Exception? ex)
    {
        if (Interlocked.CompareExchange(ref _disconnectedFlag, 1, 0) != 0)
            return;

        _isConnected = false;
        ReportUnexpectedLoss("the connection to the browser closed");
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (Interlocked.CompareExchange(ref _disconnectedFlag, 1, 0) != 0)
            return;

        _isConnected = false;
        ReportUnexpectedLoss("the browser exited");

        // Dispose transport to fault all pending CDP commands immediately
        _ = _transport.DisposeAsync().AsTask();

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    private void OnHeartbeatFailed(Exception? ex)
    {
        if (Interlocked.CompareExchange(ref _disconnectedFlag, 1, 0) != 0)
            return;

        _isConnected = false;
        ReportUnexpectedLoss("the browser stopped answering");

        // Dispose transport to fault all pending CDP commands (browser is frozen)
        _ = _transport.DisposeAsync().AsTask();

        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Says that the browser went away on its own, and what it wrote on the way.
    /// </summary>
    /// <remarks>
    /// Every command in flight is about to fail with a closed connection, which says only that the
    /// browser is gone, never why. What the browser itself wrote is the one account of that, and it
    /// goes with the process unless it is repeated here while there is still a test run to read it.
    /// A browser being shut down deliberately says nothing, because there is nothing to explain.
    /// </remarks>
    private void ReportUnexpectedLoss(string what)
    {
        if (Volatile.Read(ref _closedFlag) != 0)
            return;

        var exitCode = string.Empty;
        try
        {
            if (_process is { HasExited: true })
                exitCode = $" (exit code {_process.ExitCode})";
        }
        catch (InvalidOperationException)
        {
            // The process was disposed from under us; the reason below is worth saying regardless.
        }

        Console.Error.WriteLine($"Motus: {what}{exitCode}.{_output?.Describe()}");
    }

    private void UnregisterProcessExitHandler()
    {
        if (_process is not null)
            _process.Exited -= OnProcessExited;
    }

    private void RegisterSignalHandlers()
    {
        if (_process is null)
            return;

        if (_handleSigint)
        {
            _cancelHandler = (_, e) =>
            {
                e.Cancel = true;
                _ = CloseAsync();
            };
            Console.CancelKeyPress += _cancelHandler;
        }

        if (_handleSigterm)
        {
            _processExitHandler = (_, _) => _ = CloseAsync();
            AppDomain.CurrentDomain.ProcessExit += _processExitHandler;
        }
    }

    private void UnregisterSignalHandlers()
    {
        if (_cancelHandler is not null)
        {
            Console.CancelKeyPress -= _cancelHandler;
            _cancelHandler = null;
        }

        if (_processExitHandler is not null)
        {
            AppDomain.CurrentDomain.ProcessExit -= _processExitHandler;
            _processExitHandler = null;
        }
    }
}
