using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Motus;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Owns the live browser for the lifetime of an MCP server session. Tool calls
/// arrive as individually stateless messages, so this holder keeps the browser,
/// its isolated contexts, and the active selection between calls.
/// </summary>
/// <remarks>
/// The model is intentionally multi-context from the start: one browser holds
/// several named, isolated contexts (each with its own cookies and storage), and
/// one of them is active at any time. An implicit <see cref="DefaultContextName"/>
/// context is created on first use, so a caller that never touches named contexts
/// sees a plain single-session model. Element-addressing and per-tab state are
/// layered on later by the components that consume them.
/// </remarks>
public sealed class BrowserSessionManager : IAsyncDisposable
{
    /// <summary>The name of the context created implicitly on first use.</summary>
    public const string DefaultContextName = "default";

    private readonly McpServerLaunchOptions _options;
    private readonly ILogger<BrowserSessionManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IBrowserContext> _contexts = new(StringComparer.Ordinal);

    private IBrowser? _browser;
    private int _disposed;

    public BrowserSessionManager(McpServerLaunchOptions options, ILogger<BrowserSessionManager>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<BrowserSessionManager>.Instance;
    }

    /// <summary>The name of the context that unscoped tool calls act on.</summary>
    public string ActiveContextName { get; private set; } = DefaultContextName;

    /// <summary>Whether the browser process has been launched.</summary>
    public bool IsBrowserLaunched => _browser is not null;

    /// <summary>A snapshot of the currently open context names.</summary>
    public IReadOnlyCollection<string> ContextNames => _contexts.Keys.ToArray();

    /// <summary>
    /// Returns the live browser, launching it lazily on first use. Concurrent
    /// first callers share a single launch.
    /// </summary>
    public async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken = default)
    {
        if (_browser is not null)
        {
            return _browser;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_browser is null)
            {
                _logger.LogInformation(
                    "Launching browser (headless={Headless}, channel={Channel}).",
                    _options.Headless,
                    _options.Channel);
                _browser = await MotusLauncher.LaunchAsync(_options.ToLaunchOptions(), cancellationToken)
                    .ConfigureAwait(false);
            }

            return _browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Returns the active context, creating it (and launching the browser) on
    /// first use.
    /// </summary>
    public Task<IBrowserContext> GetOrCreateActiveContextAsync(CancellationToken cancellationToken = default)
        => GetOrCreateContextAsync(ActiveContextName, cancellationToken);

    /// <summary>
    /// Creates a new isolated context with the given name and makes it active.
    /// </summary>
    /// <exception cref="InvalidOperationException">A context with that name already exists.</exception>
    public async Task<IBrowserContext> CreateContextAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var browser = await EnsureBrowserAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_contexts.ContainsKey(name))
            {
                throw new InvalidOperationException($"A context named '{name}' already exists.");
            }

            var context = await browser.NewContextAsync().ConfigureAwait(false);
            _contexts[name] = context;
            ActiveContextName = name;
            return context;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Makes an existing context active.</summary>
    /// <exception cref="InvalidOperationException">No context with that name is open.</exception>
    public void SelectContext(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_contexts.ContainsKey(name))
        {
            throw new InvalidOperationException($"No open context named '{name}'.");
        }

        ActiveContextName = name;
    }

    /// <summary>
    /// Closes the named context and its pages. If the active context is closed,
    /// the active selection falls back to <see cref="DefaultContextName"/>.
    /// </summary>
    public async Task CloseContextAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        IBrowserContext? context;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_contexts.Remove(name, out context))
            {
                return;
            }

            if (ActiveContextName == name)
            {
                ActiveContextName = DefaultContextName;
            }
        }
        finally
        {
            _gate.Release();
        }

        await context.CloseAsync().ConfigureAwait(false);
    }

    private async Task<IBrowserContext> GetOrCreateContextAsync(string name, CancellationToken cancellationToken)
    {
        var browser = await EnsureBrowserAsync(cancellationToken).ConfigureAwait(false);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            if (_contexts.TryGetValue(name, out var existing))
            {
                return existing;
            }

            var context = await browser.NewContextAsync().ConfigureAwait(false);
            _contexts[name] = context;
            return context;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
    }

    /// <summary>
    /// Tears the session down: closes every context, then the browser. Safe to
    /// call more than once.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (var context in _contexts.Values)
            {
                try
                {
                    await context.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to close a browser context during shutdown.");
                }
            }

            _contexts.Clear();

            if (_browser is not null)
            {
                try
                {
                    await _browser.CloseAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to close the browser cleanly; forcing disposal.");
                    await _browser.DisposeAsync().ConfigureAwait(false);
                }

                _browser = null;
            }
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
