namespace Motus.Abstractions;

/// <summary>
/// Manages a pool of browser instances for concurrent test execution.
/// </summary>
public interface IBrowserPool : IAsyncDisposable
{
    /// <summary>
    /// Acquires a browser from the pool, launching a new instance if needed and capacity allows.
    /// </summary>
    Task<IBrowserLease> AcquireAsync(CancellationToken ct = default);

    /// <summary>
    /// The number of browsers currently leased out.
    /// </summary>
    int ActiveCount { get; }

    /// <summary>
    /// The number of browsers sitting idle in the pool.
    /// </summary>
    int IdleCount { get; }
}

/// <summary>
/// A leased browser instance. Disposing the lease returns the browser to the pool.
/// </summary>
public interface IBrowserLease : IAsyncDisposable
{
    /// <summary>
    /// The leased browser instance.
    /// </summary>
    IBrowser Browser { get; }
}
