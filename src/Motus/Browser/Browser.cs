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

    private readonly List<BrowserContext> _contexts = [];

    private volatile bool _isConnected;
    private ConsoleCancelEventHandler? _cancelHandler;
    private EventHandler? _processExitHandler;

    internal Browser(
        IMotusTransport transport,
        IMotusSessionRegistry registry,
        Process? process,
        string? tempUserDataDir,
        bool handleSigint,
        bool handleSigterm,
        LaunchOptions? launchOptions = null)
    {
        _transport = transport;
        _registry = registry;
        _process = process;
        _tempUserDataDir = tempUserDataDir;
        _handleSigint = handleSigint;
        _handleSigterm = handleSigterm;
        _launchOptions = launchOptions ?? new LaunchOptions();

        _transport.Disconnected += OnTransportDisconnected;
    }

    public bool IsConnected => _isConnected;

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
    }

    public async Task CloseAsync()
    {
        if (!_isConnected)
            return;

        _isConnected = false;

        UnregisterSignalHandlers();

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
        catch (CdpDisconnectedException)
        {
            // Expected: browser closes the WebSocket on shutdown
        }

        // Dispose the transport to terminate the WebSocket receive loop
        await _transport.DisposeAsync().ConfigureAwait(false);

        if (_process is not null && !_process.HasExited)
        {
            using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        UnregisterSignalHandlers();

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
        _isConnected = false;
        Disconnected?.Invoke(this, EventArgs.Empty);
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
