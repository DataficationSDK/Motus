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

    private readonly List<BrowserContext> _contexts = [];

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
        BrowserOutputDrain? output = null)
    {
        _transport = transport;
        _registry = registry;
        _process = process;
        _tempUserDataDir = tempUserDataDir;
        _handleSigint = handleSigint;
        _handleSigterm = handleSigterm;
        _launchOptions = launchOptions ?? new LaunchOptions();
        _output = output;

        _transport.Disconnected += OnTransportDisconnected;

        if (_process is not null)
        {
            _process.EnableRaisingEvents = true;
            _process.Exited += OnProcessExited;
        }
    }

    public bool IsConnected => _isConnected;

    public bool IsHealthy => _isConnected && (_process is null || !_process.HasExited);

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

        if (_process is not null)
        {
            _heartbeat = new BrowserHeartbeat(_registry.BrowserSession, OnHeartbeatFailed);
            _heartbeat.Start();
        }
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
            await EndProcessAsync().ConfigureAwait(false);
            return;
        }

        _isConnected = false;

        if (_heartbeat is not null)
            await _heartbeat.DisposeAsync().ConfigureAwait(false);

        UnregisterSignalHandlers();
        UnregisterProcessExitHandler();

        // Close all contexts first
        List<BrowserContext> contextsToClose;
        lock (_contexts)
            contextsToClose = _contexts.ToList();

        foreach (var context in contextsToClose)
        {
            await context.CloseAsync().ConfigureAwait(false);
        }

        lock (_contexts)
            _contexts.Clear();

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

        _isConnected = false;

        await _transport.DisposeAsync().ConfigureAwait(false);

        if (_process is not null)
        {
            if (!_process.HasExited)
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
