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
    private int _generation;

    public BrowserSessionManager(McpServerLaunchOptions options, ILogger<BrowserSessionManager>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<BrowserSessionManager>.Instance;
    }

    /// <summary>
    /// Launch seam used by tests to supply a controllable browser without spawning a real
    /// process. When null, the real launcher is used.
    /// </summary>
    internal Func<CancellationToken, Task<IBrowser>>? LaunchOverride { get; init; }

    /// <summary>The name of the context that unscoped tool calls act on.</summary>
    public string ActiveContextName { get; private set; } = DefaultContextName;

    /// <summary>Whether the browser process has been launched.</summary>
    public bool IsBrowserLaunched => _browser is not null;

    /// <summary>
    /// Whether a browser was launched but has since died (its process exited or its CDP transport
    /// dropped). False when no browser has been launched yet and when the current one is alive.
    /// </summary>
    public bool IsBrowserDead => _browser is { IsHealthy: false };

    /// <summary>
    /// A counter that increments each time a browser is launched, including relaunches after a
    /// crash. Layers that cache browser-bound objects (pages, contexts) compare against this to
    /// tell whether their cache belongs to the current browser or a dead one.
    /// </summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>A snapshot of the currently open context names.</summary>
    public IReadOnlyCollection<string> ContextNames => _contexts.Keys.ToArray();

    /// <summary>
    /// Returns the live browser, launching it lazily on first use. If the cached browser has
    /// died (its process crashed or its CDP transport dropped), it is disposed and a fresh one
    /// is launched in its place, so a transient browser crash recovers on the next tool call
    /// rather than wedging the session. Concurrent first callers share a single launch.
    /// </summary>
    public async Task<IBrowser> EnsureBrowserAsync(CancellationToken cancellationToken = default)
    {
        if (_browser is { IsHealthy: true })
        {
            return _browser;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            // A cached-but-dead browser must be torn down before relaunching: its contexts and
            // pages all reference a CDP session that is gone, so reusing them would keep failing.
            if (_browser is { IsHealthy: false } dead)
            {
                _logger.LogWarning("Browser is no longer healthy; disposing it and relaunching.");
                await DiscardBrowserAsync(dead).ConfigureAwait(false);
            }

            if (_browser is null)
            {
                _logger.LogInformation(
                    "Launching browser (headless={Headless}, channel={Channel}).",
                    _options.Headless,
                    _options.Channel);
                _browser = LaunchOverride is not null
                    ? await LaunchOverride(cancellationToken).ConfigureAwait(false)
                    : await MotusLauncher.LaunchAsync(_options.ToLaunchOptions(), cancellationToken)
                        .ConfigureAwait(false);
                Interlocked.Increment(ref _generation);
            }

            return _browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Disposes a dead browser and forgets it along with its now-invalid contexts. Best-effort:
    /// the browser is already gone, so a disposal failure must not propagate out of recovery.
    /// Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private async Task DiscardBrowserAsync(IBrowser dead)
    {
        try
        {
            await dead.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispose the dead browser cleanly; discarding it anyway.");
        }

        _contexts.Clear();
        ActiveContextName = DefaultContextName;
        _browser = null;
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

            var context = await browser.NewContextAsync(_options.ToContextOptions()).ConfigureAwait(false);
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

            var context = await browser.NewContextAsync(_options.ToContextOptions()).ConfigureAwait(false);
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
