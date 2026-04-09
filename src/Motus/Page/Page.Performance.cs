using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    /// <summary>
    /// The most recent performance metrics, set by <see cref="PerformanceMetricsCollector"/>
    /// after navigation or page close. Null when the hook is disabled or no collection has run.
    /// </summary>
    internal PerformanceMetrics? LastPerformanceMetrics { get; set; }
}
