using System.Diagnostics;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Manages a Chromium browser instance connected via CDP.
/// </summary>
internal sealed class Browser : IBrowser
{
    private readonly CdpTransport _transport;
    private readonly CdpSessionRegistry _registry;
    private readonly Process? _process;
    private readonly string? _tempUserDataDir;
    private readonly bool _handleSigint;
    private readonly bool _handleSigterm;

    private volatile bool _isConnected;
    private ConsoleCancelEventHandler? _cancelHandler;
    private EventHandler? _processExitHandler;

    internal Browser(
        CdpTransport transport,
        CdpSessionRegistry registry,
        Process? process,
        string? tempUserDataDir,
        bool handleSigint,
        bool handleSigterm)
    {
        _transport = transport;
        _registry = registry;
        _process = process;
        _tempUserDataDir = tempUserDataDir;
        _handleSigint = handleSigint;
        _handleSigterm = handleSigterm;

        _transport.Disconnected += OnTransportDisconnected;
    }

    public bool IsConnected => _isConnected;

    public string Version { get; private set; } = string.Empty;

    public IReadOnlyList<IBrowserContext> Contexts => Array.Empty<IBrowserContext>();

    public event EventHandler? Disconnected;

    internal async Task InitializeAsync(CancellationToken ct)
    {
        var response = await _registry.BrowserSession.SendAsync(
            "Browser.getVersion",
            CdpJsonContext.Default.BrowserGetVersionResult,
            ct);

        Version = response.Product;
        _isConnected = true;

        RegisterSignalHandlers();
    }

    public async Task CloseAsync()
    {
        if (!_isConnected)
            return;

        try
        {
            await _registry.BrowserSession.SendAsync(
                "Browser.close",
                CdpJsonContext.Default.BrowserCloseResult,
                CancellationToken.None);
        }
        catch (CdpDisconnectedException)
        {
            // Expected: browser closes the WebSocket on shutdown
        }

        if (_process is not null && !_process.HasExited)
        {
            try
            {
                await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token);
            }
            catch (OperationCanceledException)
            {
                _process.Kill(entireProcessTree: true);
            }
        }

        _isConnected = false;
    }

    public async ValueTask DisposeAsync()
    {
        UnregisterSignalHandlers();

        _isConnected = false;

        await _transport.DisposeAsync();

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

    public Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
        => throw new NotImplementedException("Browser contexts are not yet implemented (Phase 1H).");

    public Task<IPage> NewPageAsync(ContextOptions? options = null)
        => throw new NotImplementedException("Browser contexts are not yet implemented (Phase 1H).");

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
