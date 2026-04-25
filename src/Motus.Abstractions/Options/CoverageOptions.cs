namespace Motus.Abstractions;

/// <summary>
/// Configuration for the built-in code coverage collector hook.
/// </summary>
public sealed record CoverageOptions
{
    /// <summary>Whether the coverage collector is enabled. Default: false.</summary>
    public bool Enable { get; init; }

    /// <summary>Whether to collect JavaScript coverage via the CDP Profiler domain. Default: true.</summary>
    public bool IncludeJavaScript { get; init; } = true;

    /// <summary>Whether to collect CSS rule usage coverage via the CDP CSS domain. Default: true.</summary>
    public bool IncludeCss { get; init; } = true;
}
