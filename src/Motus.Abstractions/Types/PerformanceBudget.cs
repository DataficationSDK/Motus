namespace Motus.Abstractions;

/// <summary>
/// A single metric evaluation within a performance budget check.
/// </summary>
/// <param name="MetricName">The name of the metric (e.g. "LCP", "CLS").</param>
/// <param name="Threshold">The configured budget threshold.</param>
/// <param name="ActualValue">The measured value, or null if the metric was not collected.</param>
/// <param name="Passed">Whether the metric met the budget (true if actual is within threshold, or if actual is null).</param>
/// <param name="Delta">Difference between actual and threshold (positive means over budget), or null if metric was not collected.</param>
public sealed record PerformanceBudgetEntry(
    string MetricName,
    double Threshold,
    double? ActualValue,
    bool Passed,
    double? Delta);

/// <summary>
/// The result of evaluating a <see cref="PerformanceBudget"/> against collected metrics.
/// </summary>
/// <param name="Entries">Per-metric evaluation results for each configured threshold.</param>
/// <param name="Passed">True if all configured metrics are within budget.</param>
public sealed record PerformanceBudgetResult(
    IReadOnlyList<PerformanceBudgetEntry> Entries,
    bool Passed);

/// <summary>
/// Defines performance budget thresholds. Only non-null metrics are enforced.
/// </summary>
public sealed record PerformanceBudget
{
    /// <summary>Maximum Largest Contentful Paint in milliseconds.</summary>
    public double? Lcp { get; init; }

    /// <summary>Maximum First Contentful Paint in milliseconds.</summary>
    public double? Fcp { get; init; }

    /// <summary>Maximum Time to First Byte in milliseconds.</summary>
    public double? Ttfb { get; init; }

    /// <summary>Maximum Cumulative Layout Shift score.</summary>
    public double? Cls { get; init; }

    /// <summary>Maximum Interaction to Next Paint in milliseconds.</summary>
    public double? Inp { get; init; }

    /// <summary>Maximum JavaScript heap size in bytes.</summary>
    public long? JsHeapSize { get; init; }

    /// <summary>Maximum DOM node count.</summary>
    public int? DomNodeCount { get; init; }

    /// <summary>
    /// Evaluates the budget against collected performance metrics.
    /// Metrics that are null (not collected) are skipped. Only configured thresholds are checked.
    /// </summary>
    public PerformanceBudgetResult Evaluate(PerformanceMetrics metrics)
    {
        var entries = new List<PerformanceBudgetEntry>();
        bool allPassed = true;

        void Check(string name, double? threshold, double? actual)
        {
            if (threshold is null) return;
            bool passed = actual is null || actual.Value <= threshold.Value;
            if (!passed) allPassed = false;
            entries.Add(new PerformanceBudgetEntry(
                name, threshold.Value, actual, passed,
                actual.HasValue ? actual.Value - threshold.Value : null));
        }

        void CheckLong(string name, long? threshold, long? actual)
        {
            if (threshold is null) return;
            bool passed = actual is null || actual.Value <= threshold.Value;
            if (!passed) allPassed = false;
            entries.Add(new PerformanceBudgetEntry(
                name, threshold.Value, actual.HasValue ? (double)actual.Value : null, passed,
                actual.HasValue ? actual.Value - threshold.Value : null));
        }

        void CheckInt(string name, int? threshold, int? actual)
        {
            if (threshold is null) return;
            bool passed = actual is null || actual.Value <= threshold.Value;
            if (!passed) allPassed = false;
            entries.Add(new PerformanceBudgetEntry(
                name, threshold.Value, actual.HasValue ? (double)actual.Value : null, passed,
                actual.HasValue ? actual.Value - threshold.Value : null));
        }

        Check("LCP", Lcp, metrics.Lcp);
        Check("FCP", Fcp, metrics.Fcp);
        Check("TTFB", Ttfb, metrics.Ttfb);
        Check("CLS", Cls, metrics.Cls);
        Check("INP", Inp, metrics.Inp);
        CheckLong("JSHeapSize", JsHeapSize, metrics.JsHeapSize);
        CheckInt("DOMNodeCount", DomNodeCount, metrics.DomNodeCount);

        return new PerformanceBudgetResult(entries, allPassed);
    }
}
