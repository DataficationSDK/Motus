using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Factory for creating browser pools.
/// </summary>
public static class MotusBrowserPool
{
    /// <summary>
    /// Creates and warms up a new browser pool.
    /// </summary>
    public static async Task<IBrowserPool> CreateAsync(
        BrowserPoolOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new BrowserPoolOptions();
        var pool = new BrowserPool(options);
        await pool.WarmUpAsync(ct).ConfigureAwait(false);
        return pool;
    }
}
