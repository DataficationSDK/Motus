using System.Runtime.ExceptionServices;
using Motus.Abstractions;

namespace Motus.Testing;

/// <summary>
/// Manages a single browser instance for use in test fixtures.
/// Call <see cref="InitializeAsync"/> once (typically in assembly/collection setup), or
/// <see cref="EnsureInitializedAsync"/> from each test to launch the browser on first use,
/// then create isolated contexts per test via <see cref="NewContextAsync"/>.
/// If the browser crashes or becomes unresponsive, the fixture automatically
/// restarts Chrome so subsequent tests proceed against a fresh browser.
/// </summary>
/// <remarks>
/// A browser the fixture launched is ended when the process exits, if nothing disposed the fixture
/// first. That is a safety net for runs that never call <see cref="DisposeAsync"/>, not a
/// replacement for it: a test host may give exit handlers only a fraction of a second.
/// </remarks>
public sealed class BrowserFixture : IAsyncDisposable
{
    private const int NotLaunched = 0;
    private const int Live = 1;
    private const int Disposed = 2;

    private readonly Func<LaunchOptions?, Task<IBrowser>> _launch;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private IBrowser? _browser;
    private LaunchOptions? _launchOptions;
    private SemaphoreSlim _restartGate = new(1, 1);
    private int _disposed;

    // Whether the fixture has a browser to hand out, kept apart from the browser field because a
    // restart empties that field while the replacement starts. Deciding on the field alone would
    // launch a second browser beside the one the restart is about to produce.
    private int _state = NotLaunched;

    // The last launch that failed outright, kept so that every test in a run against a broken
    // environment does not pay for three more launch attempts before failing the same way.
    private ExceptionDispatchInfo? _launchFailure;
    private long _launchFailedAt;

    private int _processExitHooked;

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
    /// How long disposal waits for a restart already under way to hand the fixture back.
    /// </summary>
    private static readonly TimeSpan RestartHandoverTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Creates a fixture that launches browsers through <see cref="MotusLauncher"/>.
    /// </summary>
    public BrowserFixture()
        : this(options => MotusLauncher.LaunchAsync(options))
    {
    }

    internal BrowserFixture(Func<LaunchOptions?, Task<IBrowser>> launch)
    {
        _launch = launch;
    }

    /// <summary>
    /// How long a failed launch is reported again by <see cref="EnsureInitializedAsync"/> before
    /// another launch is attempted.
    /// </summary>
    internal TimeSpan LaunchFailureRetryAfter { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The wait after the first failed launch attempt; each later attempt waits that much longer.
    /// </summary>
    internal TimeSpan LaunchRetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Launches a browser instance with the given options, retrying on transient failures.
    /// If the fixture already has a browser, that browser is closed and replaced, so call this
    /// before tests start rather than while they are using the current one.
    /// </summary>
    public async Task InitializeAsync(LaunchOptions? options = null)
    {
        await _initGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await LaunchCoreAsync(options).ConfigureAwait(false);
        }
        finally
        {
            _initGate.Release();
        }
    }

    /// <summary>
    /// Launches the browser unless the fixture already has one, so that the first caller pays for
    /// the launch and every later caller returns at once. Safe to call from many tests at the same
    /// time: concurrent callers share a single launch.
    /// </summary>
    /// <param name="options">
    /// The options for the launch, used only when this call is the one that launches. They are
    /// ignored when the fixture already has a browser.
    /// </param>
    /// <remarks>
    /// After <see cref="DisposeAsync"/>, the next call launches a new browser. When a launch fails,
    /// the same failure is rethrown for a short while without another attempt, so a broken
    /// environment fails each test quickly rather than launching again for every one of them.
    /// </remarks>
    public async Task EnsureInitializedAsync(LaunchOptions? options = null)
    {
        if (Volatile.Read(ref _state) == Live)
            return;

        await _initGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _state) == Live)
                return;

            var failure = _launchFailure;
            if (failure is not null
                && Environment.TickCount64 - Volatile.Read(ref _launchFailedAt) < (long)LaunchFailureRetryAfter.TotalMilliseconds)
            {
                failure.Throw();
            }

            await LaunchCoreAsync(options).ConfigureAwait(false);
        }
        finally
        {
            _initGate.Release();
        }
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

        UnhookProcessExit();
        _launchFailure = null;

        // A restart in flight holds the gate while it waits on a browser to come up, and the last
        // thing a test run does is dispose the fixture. Waiting on that without a bound is how a
        // finished run fails to end: every test has passed and the process still will not exit.
        // The browser being built is about to be thrown away regardless, so the wait is worth only
        // as long as an orderly handover takes.
        var entered = await _restartGate.WaitAsync(RestartHandoverTimeout).ConfigureAwait(false);
        try
        {
            // Marked only once the gate is held, so that a launch finishing while this waited
            // cannot mark the fixture live again afterwards.
            Volatile.Write(ref _state, Disposed);

            var browser = Interlocked.Exchange(ref _browser, null);
            if (browser is not null)
            {
                browser.Disconnected -= OnBrowserDisconnected;
                await browser.DisposeAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            if (entered)
                _restartGate.Release();
        }
    }

    /// <summary>
    /// Launches a browser in place of whatever the fixture holds. The caller holds the init gate.
    /// </summary>
    private async Task LaunchCoreAsync(LaunchOptions? options)
    {
        // Reset disposed state so the fixture can be reused across runs
        // (e.g. visual runner clicking Run All a second time).
        if (Interlocked.CompareExchange(ref _disposed, 0, 1) == 1)
            _restartGate = new SemaphoreSlim(1, 1);

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // A browser launched earlier is closed rather than dropped. Dropping it left the
            // process and its profile behind for as long as the test run lasted.
            var previous = Interlocked.Exchange(ref _browser, null);
            if (previous is not null)
            {
                previous.Disconnected -= OnBrowserDisconnected;
                try { await previous.DisposeAsync().ConfigureAwait(false); }
                catch { /* it is being replaced either way */ }
            }

            _launchOptions = options;

            try
            {
                await LaunchWithRetryAsync(options).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RecordLaunchFailure(ex);
                throw;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                // Disposed while the browser was starting, by the process exiting or the run
                // ending. Nothing will use this browser and nothing else will close it.
                await DisposeStrayAsync().ConfigureAwait(false);
                throw new InvalidOperationException(
                    "The browser fixture was disposed while its browser was launching.");
            }

            _launchFailure = null;
            SubscribeDisconnected(_browser!);
            HookProcessExit();
            Volatile.Write(ref _state, Live);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private void RecordLaunchFailure(Exception ex)
    {
        Volatile.Write(ref _launchFailedAt, Environment.TickCount64);
        _launchFailure = ExceptionDispatchInfo.Capture(ex);

        if (Volatile.Read(ref _disposed) == 0)
            Volatile.Write(ref _state, NotLaunched);
    }

    private void OnBrowserDisconnected(object? sender, EventArgs e)
    {
        var dead = sender as IBrowser;
        if (dead is not null)
            dead.Disconnected -= OnBrowserDisconnected;

        _ = Task.Run(() => RestartAsync(dead));
    }

    private async Task RestartAsync(IBrowser? dead)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        await _restartGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) != 0)
                return;

            // Replaced by an explicit initialize while this waited for the gate. The browser
            // there now is not the one that died.
            if (dead is not null && !ReferenceEquals(Volatile.Read(ref _browser), dead))
                return;

            // Dispose the dead browser (best-effort)
            var old = Interlocked.Exchange(ref _browser, null);
            if (old is not null)
            {
                try { await old.DisposeAsync().ConfigureAwait(false); }
                catch { /* already dead */ }
            }

            try
            {
                await LaunchWithRetryAsync(_launchOptions).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Nothing awaits a restart, so the failure is kept for the next test that asks
                // for the browser through EnsureInitializedAsync, which launches again.
                RecordLaunchFailure(ex);
                return;
            }

            if (Volatile.Read(ref _disposed) != 0)
            {
                // Disposal came while this browser was starting and gave up waiting for the gate.
                // Nothing will use it and nothing else will close it, so it goes now rather than
                // outliving the run that launched it.
                await DisposeStrayAsync().ConfigureAwait(false);
                return;
            }

            if (_browser is not null)
                SubscribeDisconnected(_browser);
        }
        finally
        {
            _restartGate.Release();
        }
    }

    private async Task DisposeStrayAsync()
    {
        var stray = Interlocked.Exchange(ref _browser, null);
        if (stray is not null)
        {
            try { await stray.DisposeAsync().ConfigureAwait(false); }
            catch { /* nothing left to salvage */ }
        }
    }

    private async Task LaunchWithRetryAsync(LaunchOptions? options)
    {
        for (int attempt = 1; attempt <= MaxLaunchAttempts; attempt++)
        {
            try
            {
                _browser = await _launch(options).ConfigureAwait(false);
                return;
            }
            catch when (attempt < MaxLaunchAttempts)
            {
                await Task.Delay(LaunchRetryDelay * attempt).ConfigureAwait(false);
            }
        }
    }

    private void SubscribeDisconnected(IBrowser browser)
        => browser.Disconnected += OnBrowserDisconnected;

    private void HookProcessExit()
    {
        if (Interlocked.Exchange(ref _processExitHooked, 1) == 0)
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    private void UnhookProcessExit()
    {
        if (Interlocked.Exchange(ref _processExitHooked, 0) == 1)
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs e) => TerminateForProcessExit();

    /// <summary>
    /// Ends the browser synchronously as the process exits, for a run that never disposed the
    /// fixture, such as an MSTest assembly with no <c>[AssemblyCleanup]</c>.
    /// </summary>
    /// <remarks>
    /// The fixture is marked disposed before the browser is touched. Killing the browser raises its
    /// exit, and a fixture that still considered itself live would answer by launching another.
    /// Nothing is awaited: a test host can end the process very shortly after exit begins.
    /// </remarks>
    internal void TerminateForProcessExit()
    {
        Interlocked.Exchange(ref _disposed, 1);
        Volatile.Write(ref _state, Disposed);
        _launchFailure = null;
        UnhookProcessExit();

        var browser = Interlocked.Exchange(ref _browser, null);
        if (browser is null)
            return;

        browser.Disconnected -= OnBrowserDisconnected;

        try
        {
            if (browser is global::Motus.Browser launched)
                launched.TerminateForProcessExit();
            else
                browser.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // The process is ending; there is no one left to tell.
        }
    }
}
