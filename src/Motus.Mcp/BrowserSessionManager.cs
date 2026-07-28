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
///
/// The browser is either started here or connected to, and the difference runs through
/// everything below. A browser that was already running belongs to somebody else: its contexts are
/// adopted rather than created, they are never closed on the way out, and the browser itself is
/// disconnected from rather than ended.
/// </remarks>
public sealed class BrowserSessionManager : IAsyncDisposable
{
    /// <summary>The name of the context created implicitly on first use.</summary>
    public const string DefaultContextName = "default";

    /// <summary>
    /// A context this session holds, and whether it was found in the browser rather than created
    /// here. An adopted context belongs to whoever was using the browser first, so closing it is
    /// not this session's to do.
    /// </summary>
    private readonly record struct HeldContext(IBrowserContext Context, bool Adopted);

    private readonly McpServerLaunchOptions _options;
    private readonly ILogger<BrowserSessionManager> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, HeldContext> _contexts = new(StringComparer.Ordinal);

    private IBrowser? _browser;
    private string? _endpoint;
    private int _disposed;
    private int _generation;

    public BrowserSessionManager(McpServerLaunchOptions options, ILogger<BrowserSessionManager>? logger = null)
    {
        _options = options;
        _endpoint = options.Endpoint;
        _logger = logger ?? NullLogger<BrowserSessionManager>.Instance;
    }

    /// <summary>
    /// Launch seam used by tests to supply a controllable browser without spawning a real
    /// process. When null, the real launcher is used.
    /// </summary>
    internal Func<CancellationToken, Task<IBrowser>>? LaunchOverride { get; init; }

    /// <summary>
    /// Connect seam used by tests to supply a controllable browser without a real endpoint. Takes
    /// the endpoint being connected to. When null, the real connector is used.
    /// </summary>
    internal Func<string, CancellationToken, Task<IBrowser>>? ConnectOverride { get; init; }

    /// <summary>The name of the context that unscoped tool calls act on.</summary>
    public string ActiveContextName { get; private set; } = DefaultContextName;

    /// <summary>Whether the browser process has been launched.</summary>
    public bool IsBrowserLaunched => _browser is not null;

    /// <summary>
    /// The endpoint this session attaches to, or null when it starts a browser of its own.
    /// </summary>
    public string? Endpoint => _endpoint;

    /// <summary>
    /// Whether the live browser is one this session connected to rather than started. False before
    /// a browser exists, whatever the configuration says, because ownership is a fact about the
    /// browser in hand.
    /// </summary>
    public bool IsAttached => _browser is { OwnsProcess: false };

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
    /// Returns the live browser, acquiring it lazily on first use: started here, or connected to
    /// when an endpoint is configured. If the cached browser has died (its process crashed or its
    /// CDP transport dropped), it is disposed and acquired again the same way, so a transient
    /// browser crash recovers on the next tool call rather than wedging the session. Concurrent
    /// first callers share a single acquisition.
    /// </summary>
    /// <remarks>
    /// Recovery reconnects rather than starting a replacement when an endpoint is configured. A
    /// browser this session did not start cannot be started again by it, and the endpoint is the
    /// only thing that could still be there to answer.
    /// </remarks>
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

            // A cached-but-dead browser must be torn down before acquiring another: its contexts
            // and pages all reference a CDP session that is gone, so reusing them would keep
            // failing.
            if (_browser is { IsHealthy: false } dead)
            {
                _logger.LogWarning("Browser is no longer healthy; disposing it and acquiring another.");
                await DiscardBrowserAsync(dead).ConfigureAwait(false);
            }

            _browser ??= await AcquireBrowserAsync(cancellationToken).ConfigureAwait(false);
            return _browser;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Starts a browser, or connects to the configured endpoint, and counts the result as a new
    /// generation. Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private async Task<IBrowser> AcquireBrowserAsync(CancellationToken cancellationToken)
    {
        IBrowser browser;

        if (_endpoint is { } endpoint)
        {
            _logger.LogInformation("Connecting to the browser at {Endpoint}.", endpoint);
            browser = ConnectOverride is not null
                ? await ConnectOverride(endpoint, cancellationToken).ConfigureAwait(false)
                : await MotusLauncher.ConnectAsync(endpoint, _options.ToConnectOptions(), cancellationToken)
                    .ConfigureAwait(false);
        }
        else
        {
            _logger.LogInformation(
                "Launching browser (headless={Headless}, channel={Channel}).",
                _options.Headless,
                _options.Channel);
            browser = LaunchOverride is not null
                ? await LaunchOverride(cancellationToken).ConfigureAwait(false)
                : await MotusLauncher.LaunchAsync(_options.ToLaunchOptions(), cancellationToken)
                    .ConfigureAwait(false);
        }

        Interlocked.Increment(ref _generation);
        return browser;
    }

    /// <summary>
    /// Points this session at a browser that is already running, releasing whatever browser it was
    /// holding. A browser started here is closed; one connected to earlier is only disconnected
    /// from.
    /// </summary>
    /// <remarks>
    /// The generation counter moves, which is how every page and snapshot cached against the old
    /// browser is invalidated: the layers above compare their cached generation against this one
    /// and re-resolve when it has moved.
    /// </remarks>
    public async Task<IBrowser> AttachAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();

            await ReleaseBrowserAsync().ConfigureAwait(false);
            _endpoint = endpoint;

            // Deliberately not guarded: a failed connect leaves the session with no browser and the
            // endpoint recorded, so the next tool call retries it and the caller sees why.
            _browser = await AcquireBrowserAsync(cancellationToken).ConfigureAwait(false);
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
    /// Lets go of the live browser: closes the contexts this session created, leaves the ones it
    /// adopted alone, and then either ends the browser or only disconnects from it depending on
    /// whether this session started it. Must be called while holding <see cref="_gate"/>.
    /// </summary>
    private async Task ReleaseBrowserAsync()
    {
        foreach (var held in _contexts.Values)
        {
            if (held.Adopted)
                continue;

            try
            {
                await held.Context.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to close a browser context during shutdown.");
            }
        }

        _contexts.Clear();
        ActiveContextName = DefaultContextName;

        if (_browser is not { } browser)
            return;

        _browser = null;

        try
        {
            // CloseAsync already disconnects rather than terminating when the process is not owned,
            // so this branch is about intent being visible at the call site rather than about a
            // difference in what reaches the browser.
            if (browser.OwnsProcess)
                await browser.CloseAsync().ConfigureAwait(false);
            else
                await browser.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to release the browser cleanly; forcing disposal.");
            try
            {
                await browser.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception disposeEx)
            {
                _logger.LogWarning(disposeEx, "Disposing the browser also failed; discarding it anyway.");
            }
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

            // A named context is always a new one, including against an attached browser. Some
            // embedded Chromium hosts cannot create browser contexts at all; that failure surfaces
            // to the caller rather than being papered over, because silently handing back the
            // browser's existing context would not be the isolation the caller asked for.
            var context = await browser.NewContextAsync(_options.ToContextOptions()).ConfigureAwait(false);
            _contexts[name] = new HeldContext(context, Adopted: false);
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
    /// <remarks>
    /// A context adopted from a browser this session connected to is only let go of, not closed.
    /// It was open before this session arrived and its tabs are somebody's working state.
    /// </remarks>
    public async Task CloseContextAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        HeldContext held;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_contexts.Remove(name, out held))
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

        if (!held.Adopted)
            await held.Context.CloseAsync().ConfigureAwait(false);
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
                return existing.Context;
            }

            // Against a browser that was already running, the default context is the one it is
            // already using. Creating a fresh one instead would open an empty window beside
            // everything the caller attached in order to reach, and some embedded Chromium hosts
            // cannot create a context at all.
            if (name == DefaultContextName
                && !browser.OwnsProcess
                && browser.Contexts.Count > 0)
            {
                var adopted = browser.Contexts[0];
                _contexts[name] = new HeldContext(adopted, Adopted: true);
                return adopted;
            }

            var context = await browser.NewContextAsync(_options.ToContextOptions()).ConfigureAwait(false);
            _contexts[name] = new HeldContext(context, Adopted: false);
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
    /// Tears the session down: closes the contexts it created, then releases the browser. A browser
    /// this session connected to is disconnected from and keeps running, along with everything that
    /// was open in it. Safe to call more than once.
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
            await ReleaseBrowserAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
