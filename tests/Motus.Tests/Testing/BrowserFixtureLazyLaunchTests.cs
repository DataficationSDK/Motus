using Motus.Abstractions;
using Motus.Testing;

namespace Motus.Tests.Testing;

/// <summary>
/// Drives the fixture's launch, restart, and shutdown paths against stand-in browsers, so the
/// races between them can be set up exactly rather than hoped for.
/// </summary>
[TestClass]
public class BrowserFixtureLazyLaunchTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    [TestMethod]
    public async Task EnsureInitialized_ConcurrentCallers_LaunchOnce()
    {
        var launcher = new FakeLauncher(delay: TimeSpan.FromMilliseconds(50));
        await using var fixture = launcher.CreateFixture();

        await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() => fixture.EnsureInitializedAsync())));

        Assert.AreEqual(1, launcher.Launches);
        Assert.AreSame(launcher.Launched[0], fixture.Browser);
    }

    [TestMethod]
    public async Task EnsureInitialized_AfterExplicitInitialize_IsNoOp()
    {
        var launcher = new FakeLauncher();
        await using var fixture = launcher.CreateFixture();

        await fixture.InitializeAsync();
        await fixture.EnsureInitializedAsync();

        Assert.AreEqual(1, launcher.Launches);
    }

    [TestMethod]
    public async Task EnsureInitialized_PassesItsOptionsToTheLaunch()
    {
        var launcher = new FakeLauncher();
        await using var fixture = launcher.CreateFixture();
        var options = new LaunchOptions { SlowMo = 25 };

        await fixture.EnsureInitializedAsync(options);

        Assert.AreSame(options, launcher.LastOptions);
    }

    [TestMethod]
    public async Task EnsureInitialized_AfterDispose_LaunchesAgain()
    {
        var launcher = new FakeLauncher();
        await using var fixture = launcher.CreateFixture();

        await fixture.EnsureInitializedAsync();
        await fixture.DisposeAsync();
        await fixture.EnsureInitializedAsync();

        Assert.AreEqual(2, launcher.Launches);
        Assert.AreEqual(1, launcher.Launched[0].DisposeCount, "the first browser should have been closed");
        Assert.AreSame(launcher.Launched[1], fixture.Browser);
    }

    [TestMethod]
    public async Task Initialize_WhenLive_ClosesThePreviousBrowser()
    {
        var launcher = new FakeLauncher();
        await using var fixture = launcher.CreateFixture();

        await fixture.InitializeAsync();
        await fixture.InitializeAsync();

        Assert.AreEqual(2, launcher.Launches);
        Assert.AreEqual(1, launcher.Launched[0].DisposeCount, "the replaced browser should not be left running");
        Assert.IsFalse(launcher.Launched[0].HasDisconnectedHandler, "the replaced browser should not trigger restarts");
        Assert.AreSame(launcher.Launched[1], fixture.Browser);
    }

    [TestMethod]
    public async Task EnsureInitialized_DuringCrashRestart_DoesNotLaunchASecondBrowser()
    {
        var restartGate = new TaskCompletionSource();
        var launcher = new FakeLauncher(onLaunch: n => n == 2 ? restartGate.Task : Task.CompletedTask);
        await using var fixture = launcher.CreateFixture();

        await fixture.EnsureInitializedAsync();
        launcher.Launched[0].RaiseDisconnected();
        await WaitUntilAsync(() => launcher.Launches == 2);

        // The restart is now part way through launching the replacement.
        await fixture.EnsureInitializedAsync();
        Assert.AreEqual(2, launcher.Launches, "a lazy launch should not start beside a restart");

        restartGate.SetResult();
        await WaitUntilAsync(() => ReferenceEquals(TryGetBrowser(fixture), launcher.Launched[1]));
        Assert.AreEqual(2, launcher.Launches);
    }

    [TestMethod]
    public async Task EnsureInitialized_LaunchFailure_IsRethrownWithoutRelaunching()
    {
        var launcher = new FakeLauncher(onLaunch: _ => throw new InvalidOperationException("no browser here"));
        await using var fixture = launcher.CreateFixture();

        var first = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => fixture.EnsureInitializedAsync());
        var attempts = launcher.Launches;
        var second = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => fixture.EnsureInitializedAsync());

        Assert.AreEqual(3, attempts, "the first launch should make its usual three attempts");
        Assert.AreEqual(attempts, launcher.Launches, "a recent failure should be reported, not repeated");
        Assert.AreEqual(first.Message, second.Message);
    }

    [TestMethod]
    public async Task EnsureInitialized_LaunchFailure_IsRetriedOnceTheWindowPasses()
    {
        var failing = true;
        var launcher = new FakeLauncher(onLaunch: _ => failing ? throw new InvalidOperationException("no browser here") : Task.CompletedTask);
        await using var fixture = launcher.CreateFixture();
        fixture.LaunchFailureRetryAfter = TimeSpan.Zero;

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => fixture.EnsureInitializedAsync());
        failing = false;
        await fixture.EnsureInitializedAsync();

        Assert.AreEqual(4, launcher.Launches);
    }

    [TestMethod]
    public async Task EnsureInitialized_LaunchFailure_IsRetriedAfterDispose()
    {
        var failing = true;
        var launcher = new FakeLauncher(onLaunch: _ => failing ? throw new InvalidOperationException("no browser here") : Task.CompletedTask);
        await using var fixture = launcher.CreateFixture();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => fixture.EnsureInitializedAsync());
        await fixture.DisposeAsync();
        failing = false;
        await fixture.EnsureInitializedAsync();

        Assert.AreEqual(4, launcher.Launches);
    }

    [TestMethod]
    public async Task FailedRestart_IsReportedAndRelaunchedByTheNextEnsure()
    {
        var failing = false;
        var launcher = new FakeLauncher(onLaunch: _ => failing ? throw new InvalidOperationException("restart failed") : Task.CompletedTask);
        await using var fixture = launcher.CreateFixture();

        await fixture.EnsureInitializedAsync();
        failing = true;
        launcher.Launched[0].RaiseDisconnected();

        // Once the restart has given up, the next test hears why rather than finding no browser.
        await WaitUntilAsync(async () =>
        {
            try { await fixture.EnsureInitializedAsync(); return false; }
            catch (InvalidOperationException ex) { return ex.Message == "restart failed"; }
        });

        failing = false;
        fixture.LaunchFailureRetryAfter = TimeSpan.Zero;
        await fixture.EnsureInitializedAsync();

        Assert.AreEqual(2, launcher.Launched.Count);
        Assert.AreSame(launcher.Launched[1], fixture.Browser);
    }

    [TestMethod]
    public async Task ProcessExitCleanup_ClosesTheBrowserWithoutRestarting()
    {
        var launcher = new FakeLauncher();
        await using var fixture = launcher.CreateFixture();
        await fixture.EnsureInitializedAsync();
        var browser = launcher.Launched[0];

        fixture.TerminateForProcessExit();

        // Killing a browser is what raises its disconnect; the fixture must not answer it.
        browser.RaiseDisconnected();
        await Task.Delay(100);

        Assert.AreEqual(1, browser.DisposeCount, "the browser should be closed on the way out");
        Assert.AreEqual(1, launcher.Launches, "shutting down should not start a replacement");
        Assert.IsFalse(browser.HasDisconnectedHandler);
    }

    private static IBrowser? TryGetBrowser(BrowserFixture fixture)
    {
        try { return fixture.Browser; }
        catch (InvalidOperationException) { return null; }
    }

    private static Task WaitUntilAsync(Func<bool> condition)
        => WaitUntilAsync(() => Task.FromResult(condition()));

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow + WaitLimit;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Timed out waiting for the fixture.");
            await Task.Delay(10);
        }
    }

    private sealed class FakeLauncher(TimeSpan? delay = null, Func<int, Task>? onLaunch = null)
    {
        private int _launches;

        public int Launches => Volatile.Read(ref _launches);

        public List<FakeBrowser> Launched { get; } = [];

        public LaunchOptions? LastOptions { get; private set; }

        public BrowserFixture CreateFixture() => new(LaunchAsync) { LaunchRetryDelay = TimeSpan.Zero };

        private async Task<IBrowser> LaunchAsync(LaunchOptions? options)
        {
            var n = Interlocked.Increment(ref _launches);
            LastOptions = options;

            if (delay is { } d)
                await Task.Delay(d);
            if (onLaunch is not null)
                await onLaunch(n);

            var browser = new FakeBrowser();
            lock (Launched)
                Launched.Add(browser);
            return browser;
        }
    }

    private sealed class FakeBrowser : IBrowser
    {
        private int _disposeCount;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public bool HasDisconnectedHandler => Disconnected is not null;

        public bool IsConnected => true;

        public bool OwnsProcess => true;

        public IReadOnlyList<IBrowserContext> Contexts => [];

        public string Version => "fake";

        public event EventHandler? Disconnected;

        public void RaiseDisconnected() => Disconnected?.Invoke(this, EventArgs.Empty);

        public Task<IBrowserContext> NewContextAsync(ContextOptions? options = null) => throw new NotSupportedException();

        public Task<IPage> NewPageAsync(ContextOptions? options = null) => throw new NotSupportedException();

        public Task CloseAsync() => Task.CompletedTask;

        public Task DisconnectAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            return ValueTask.CompletedTask;
        }
    }
}
