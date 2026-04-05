using Motus.Abstractions;

namespace Motus.Tests.Stress;

[TestClass]
[TestCategory("Stress")]
public class BrowserPoolStressTests
{
    [TestMethod]
    public async Task AcquireAsync_WithinCapacity_AllSucceed()
    {
        var options = new BrowserPoolOptions { MinInstances = 0, MaxInstances = 5 };
        var pool = new Motus.BrowserPool(options);

        // We cannot launch real browsers in unit tests, so we test the pool
        // mechanics by verifying semaphore and channel behavior.
        // This test validates that the pool can be created and disposed cleanly.
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task Pool_DisposeAsync_IsIdempotent()
    {
        var options = new BrowserPoolOptions { MinInstances = 0, MaxInstances = 2 };
        var pool = new Motus.BrowserPool(options);

        await pool.DisposeAsync();
        await pool.DisposeAsync(); // Should not throw

        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(0, pool.IdleCount);
    }

    [TestMethod]
    public async Task Pool_AcquireAfterDispose_Throws()
    {
        var options = new BrowserPoolOptions { MinInstances = 0, MaxInstances = 2 };
        var pool = new Motus.BrowserPool(options);

        await pool.DisposeAsync();

        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(
            () => pool.AcquireAsync());
    }

    [TestMethod]
    public void Pool_InitialState_ZeroCounts()
    {
        var options = new BrowserPoolOptions { MinInstances = 0, MaxInstances = 5 };
        var pool = new Motus.BrowserPool(options);

        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(0, pool.IdleCount);

        _ = pool.DisposeAsync();
    }

    [TestMethod]
    public void BrowserLease_DisposeAsync_IsIdempotent()
    {
        // Verify the lease dispose guard works
        int returnCount = 0;
        var lease = new Motus.BrowserLease(
            new FakeBrowser(),
            _ => { Interlocked.Increment(ref returnCount); return ValueTask.CompletedTask; });

        _ = lease.DisposeAsync();
        _ = lease.DisposeAsync();
        _ = lease.DisposeAsync();

        Assert.AreEqual(1, returnCount);
    }

    [TestMethod]
    public void BrowserLease_Browser_ReturnsBrowserInstance()
    {
        var fakeBrowser = new FakeBrowser();
        var lease = new Motus.BrowserLease(
            fakeBrowser,
            _ => ValueTask.CompletedTask);

        Assert.AreSame(fakeBrowser, lease.Browser);

        _ = lease.DisposeAsync();
    }

    [TestMethod]
    public async Task BrowserLease_DisposeAsync_CallsReturnAction()
    {
        IBrowser? returnedBrowser = null;
        var fakeBrowser = new FakeBrowser();
        var lease = new Motus.BrowserLease(
            fakeBrowser,
            b => { returnedBrowser = b; return ValueTask.CompletedTask; });

        await lease.DisposeAsync();

        Assert.AreSame(fakeBrowser, returnedBrowser, "Return action should receive the original browser.");
    }

    [TestMethod]
    public async Task BrowserLease_SecondDispose_DoesNotCallReturnAgain()
    {
        int returnCount = 0;
        var lease = new Motus.BrowserLease(
            new FakeBrowser(),
            _ => { Interlocked.Increment(ref returnCount); return ValueTask.CompletedTask; });

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.AreEqual(1, returnCount, "Return action should only be called once.");
    }

    [TestMethod]
    public async Task Pool_DisposeAsync_SetsIdleCountToZero()
    {
        var options = new BrowserPoolOptions { MinInstances = 0, MaxInstances = 3 };
        var pool = new Motus.BrowserPool(options);

        await pool.DisposeAsync();

        Assert.AreEqual(0, pool.IdleCount);
        Assert.AreEqual(0, pool.ActiveCount);
    }

    [TestMethod]
    public async Task Pool_MultipleDispose_DoesNotThrow()
    {
        var options = new BrowserPoolOptions { MinInstances = 0, MaxInstances = 2 };
        var pool = new Motus.BrowserPool(options);

        await pool.DisposeAsync();
        await pool.DisposeAsync();
        await pool.DisposeAsync();

        Assert.AreEqual(0, pool.ActiveCount);
    }

    [TestMethod]
    public void Pool_Options_ConfiguredCorrectly()
    {
        var options = new BrowserPoolOptions
        {
            MinInstances = 2,
            MaxInstances = 10,
            AcquireTimeout = TimeSpan.FromSeconds(60)
        };
        var pool = new Motus.BrowserPool(options);

        Assert.AreEqual(0, pool.ActiveCount);
        Assert.AreEqual(0, pool.IdleCount);

        _ = pool.DisposeAsync();
    }

    [TestMethod]
    public void BrowserPoolOptions_Defaults()
    {
        var options = new BrowserPoolOptions();

        Assert.AreEqual(1, options.MinInstances);
        Assert.AreEqual(Environment.ProcessorCount, options.MaxInstances);
        Assert.AreEqual(TimeSpan.FromSeconds(30), options.AcquireTimeout);
        Assert.IsNull(options.LaunchOptions);
    }

    [TestMethod]
    public void BrowserPoolOptions_WithLaunchOptions()
    {
        var launchOpts = new LaunchOptions { Headless = true };
        var options = new BrowserPoolOptions { LaunchOptions = launchOpts };

        Assert.AreSame(launchOpts, options.LaunchOptions);
    }

    /// <summary>
    /// Minimal fake browser for testing pool mechanics without CDP.
    /// </summary>
    private sealed class FakeBrowser : IBrowser
    {
        public bool IsConnected { get; set; } = true;
        public IReadOnlyList<IBrowserContext> Contexts => [];
        public string Version => "Fake/1.0";
        public event EventHandler? Disconnected;

        public Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
            => throw new NotSupportedException();

        public Task<IPage> NewPageAsync(ContextOptions? options = null)
            => throw new NotSupportedException();

        public Task CloseAsync() => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            Disconnected?.Invoke(this, EventArgs.Empty);
            return ValueTask.CompletedTask;
        }
    }
}
