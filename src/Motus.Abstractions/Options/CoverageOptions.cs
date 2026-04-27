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

    /// <summary>
    /// Minimum acceptable JavaScript line coverage percentage (0-100). When set and the
    /// aggregated run line coverage falls below this value, the run fails with a non-zero exit code.
    /// </summary>
    public double? JsLineThreshold { get; init; }

    /// <summary>
    /// Minimum acceptable JavaScript function coverage percentage (0-100). Reserved for
    /// future use; current aggregator emits line-level stats only.
    /// </summary>
    public double? JsFunctionThreshold { get; init; }

    /// <summary>
    /// Minimum acceptable CSS rule usage percentage (0-100). When set and the aggregated
    /// run CSS coverage falls below this value, the run fails with a non-zero exit code.
    /// </summary>
    public double? CssRuleThreshold { get; init; }
}
