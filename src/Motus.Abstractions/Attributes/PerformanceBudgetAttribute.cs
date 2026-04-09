namespace Motus.Abstractions;

/// <summary>
/// Declares performance budget thresholds for a test method or class.
/// Only specified metrics (values >= 0) are enforced. Method-level attributes
/// override class-level attributes.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Class,
    AllowMultiple = false,
    Inherited = true)]
public sealed class PerformanceBudgetAttribute : Attribute
{
    /// <summary>Maximum Largest Contentful Paint in milliseconds. -1 means not set.</summary>
    public double Lcp { get; set; } = -1;

    /// <summary>Maximum First Contentful Paint in milliseconds. -1 means not set.</summary>
    public double Fcp { get; set; } = -1;

    /// <summary>Maximum Time to First Byte in milliseconds. -1 means not set.</summary>
    public double Ttfb { get; set; } = -1;

    /// <summary>Maximum Cumulative Layout Shift score. -1 means not set.</summary>
    public double Cls { get; set; } = -1;

    /// <summary>Maximum Interaction to Next Paint in milliseconds. -1 means not set.</summary>
    public double Inp { get; set; } = -1;

    /// <summary>Maximum JavaScript heap size in bytes. -1 means not set.</summary>
    public long JsHeapSize { get; set; } = -1;

    /// <summary>Maximum DOM node count. -1 means not set.</summary>
    public int DomNodeCount { get; set; } = -1;

    /// <summary>
    /// Converts the attribute values to a <see cref="PerformanceBudget"/> record.
    /// Sentinel values (-1) are mapped to null (not enforced).
    /// </summary>
    public PerformanceBudget ToBudget() => new()
    {
        Lcp = Lcp >= 0 ? Lcp : null,
        Fcp = Fcp >= 0 ? Fcp : null,
        Ttfb = Ttfb >= 0 ? Ttfb : null,
        Cls = Cls >= 0 ? Cls : null,
        Inp = Inp >= 0 ? Inp : null,
        JsHeapSize = JsHeapSize >= 0 ? JsHeapSize : null,
        DomNodeCount = DomNodeCount >= 0 ? DomNodeCount : null,
    };
}
