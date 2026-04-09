namespace Motus.Abstractions;

/// <summary>
/// Configuration for the built-in performance metrics collector hook.
/// </summary>
public sealed record PerformanceOptions
{
    /// <summary>Whether the performance metrics collector is enabled. Default: false.</summary>
    public bool Enable { get; init; }

    /// <summary>Whether to collect metrics after each navigation. Default: true.</summary>
    public bool CollectAfterNavigation { get; init; } = true;
}
