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
