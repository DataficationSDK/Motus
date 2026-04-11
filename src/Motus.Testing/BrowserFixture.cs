using Motus.Abstractions;

namespace Motus.Testing;

/// <summary>
/// Manages a single browser instance for use in test fixtures.
/// Call <see cref="InitializeAsync"/> once (typically in assembly/collection setup),
/// then create isolated contexts per test via <see cref="NewContextAsync"/>.
/// If the browser crashes or becomes unresponsive, the fixture automatically
/// restarts Chrome so subsequent tests proceed against a fresh browser.
/// </summary>
public sealed class BrowserFixture : IAsyncDisposable
{
    private IBrowser? _browser;
    private LaunchOptions? _launchOptions;
    private SemaphoreSlim _restartGate = new(1, 1);
    private int _disposed;

    /// <summary>
    /// Limits concurrent browser contexts to prevent Chrome from becoming
    /// unresponsive under heavy parallel test load. Chrome's renderer process
    /// model can stall when too many targets compete for resources.
    /// </summary>
    private static readonly SemaphoreSlim s_contextThrottle = new(4, 4);

    /// <summary>
    /// Maximum number of launch attempts before giving up.
    /// Browser startup can fail transiently on CI runners or when antivirus
    /// delays process creation, so retrying avoids flaky test runs.
    /// </summary>
    private const int MaxLaunchAttempts = 3;

    /// <summary>
    /// Launches a browser instance with the given options, retrying on transient failures.
    /// </summary>
    public async Task InitializeAsync(LaunchOptions? options = null)
    {
        // Reset disposed state so the fixture can be reused across runs
        // (e.g. visual runner clicking Run All a second time).
        if (Interlocked.CompareExchange(ref _disposed, 0, 1) == 1)
            _restartGate = new SemaphoreSlim(1, 1);

        _launchOptions = options;
        await LaunchWithRetryAsync(options).ConfigureAwait(false);
        SubscribeDisconnected(_browser!);
    }

    /// <summary>
    /// The launched browser instance.
    /// </summary>
    public IBrowser Browser => _browser ?? throw new InvalidOperationException(
        "Browser not initialized. Call InitializeAsync first.");

    /// <summary>
    /// Creates a new isolated browser context. If a restart is in progress
    /// (due to a browser crash), callers block until the new browser is ready.
    /// The context throttle limits concurrent contexts to avoid overwhelming Chrome.
    /// </summary>
    public async Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
    {
        await s_contextThrottle.WaitAsync().ConfigureAwait(false);

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Browser.NewContextAsync(options).ConfigureAwait(false);
        }
        catch
        {
            s_contextThrottle.Release();
            throw;
        }
        finally
        {
            _restartGate.Release();
        }
    }

    /// <summary>
    /// Closes a context and releases its concurrency throttle slot.
    /// Always releases the slot even if close fails (browser crashed).
    /// </summary>
    public async Task CloseContextAsync(IBrowserContext context)
    {
        try
        {
            await context.CloseAsync().ConfigureAwait(false);
        }
        finally
        {
            s_contextThrottle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
            return;

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_browser is not null)
            {
                _browser.Disconnected -= OnBrowserDisconnected;
                await _browser.DisposeAsync().ConfigureAwait(false);
                _browser = null;
            }
        }
        finally
        {
            _restartGate.Release();
            _restartGate.Dispose();
        }
    }

    private void OnBrowserDisconnected(object? sender, EventArgs e)
    {
        if (sender is IBrowser old)
            old.Disconnected -= OnBrowserDisconnected;

        _ = Task.Run(RestartAsync);
    }

    private async Task RestartAsync()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            // Dispose the dead browser (best-effort)
            var old = Interlocked.Exchange(ref _browser, null);
            if (old is not null)
            {
                try { await old.DisposeAsync().ConfigureAwait(false); }
                catch { /* already dead */ }
            }

            await LaunchWithRetryAsync(_launchOptions).ConfigureAwait(false);

            if (_browser is not null)
                SubscribeDisconnected(_browser);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private async Task LaunchWithRetryAsync(LaunchOptions? options)
    {
        for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
        {
            try
            {
                _browser = await MotusLauncher.LaunchAsync(options).ConfigureAwait(false);
                return;
            }
            catch when (attempt < MaxLaunchAttempts)
            {
                await Task.Delay(1000 * attempt).ConfigureAwait(false);
            }
        }
    }

    private void SubscribeDisconnected(IBrowser browser)
        => browser.Disconnected += OnBrowserDisconnected;
}
