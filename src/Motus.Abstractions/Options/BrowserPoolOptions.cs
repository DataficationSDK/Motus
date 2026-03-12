namespace Motus.Abstractions;

/// <summary>
/// Configuration for a browser pool.
/// </summary>
public sealed record BrowserPoolOptions
{
    /// <summary>Minimum number of browser instances to keep warm.</summary>
    public int MinInstances { get; init; } = 1;

    /// <summary>Maximum number of concurrent browser instances.</summary>
    public int MaxInstances { get; init; } = Environment.ProcessorCount;

    /// <summary>How long to wait when all browsers are busy before timing out.</summary>
    public TimeSpan AcquireTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Launch options applied to each browser in the pool.</summary>
    public LaunchOptions? LaunchOptions { get; init; }
}
