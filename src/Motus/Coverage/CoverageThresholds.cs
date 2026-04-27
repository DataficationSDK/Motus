using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Per-threshold pass/fail entry produced by <see cref="CoverageThresholds.Evaluate"/>.
/// </summary>
internal sealed record CoverageThresholdEntry(string MetricName, double Threshold, double Actual, bool Passed);

/// <summary>
/// Outcome of evaluating coverage thresholds against an aggregated <see cref="CoverageData"/>.
/// </summary>
internal sealed record CoverageThresholdResult(IReadOnlyList<CoverageThresholdEntry> Entries)
{
    public bool Passed => Entries.All(e => e.Passed);
    public IEnumerable<CoverageThresholdEntry> Failed => Entries.Where(e => !e.Passed);
}

/// <summary>
/// Evaluates coverage threshold options against an aggregated coverage snapshot.
/// </summary>
internal static class CoverageThresholds
{
    internal static CoverageThresholdResult Evaluate(CoverageData data, CoverageOptions options)
    {
        var entries = new List<CoverageThresholdEntry>();

        if (options.JsLineThreshold is double jsLine)
        {
            var actual = data.Summary.LinePercentage;
            entries.Add(new CoverageThresholdEntry("js.lines", jsLine, actual, actual >= jsLine));
        }

        if (options.CssRuleThreshold is double cssRule)
        {
            var actual = data.Summary.CssPercentage;
            entries.Add(new CoverageThresholdEntry("css.rules", cssRule, actual, actual >= cssRule));
        }

        return new CoverageThresholdResult(entries);
    }
}
