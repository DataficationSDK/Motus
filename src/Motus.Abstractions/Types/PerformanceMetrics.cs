namespace Motus.Abstractions;

/// <summary>
/// An individual layout shift entry observed during page execution.
/// </summary>
/// <param name="Score">The layout shift score for this entry.</param>
/// <param name="SourceElements">Tag names of the DOM elements that shifted.</param>
public sealed record LayoutShiftEntry(
    double Score,
    IReadOnlyList<string> SourceElements);

/// <summary>
/// Core Web Vitals and supplementary performance metrics collected during test execution.
/// </summary>
/// <param name="Lcp">Largest Contentful Paint in milliseconds, or null if not yet observed.</param>
/// <param name="Fcp">First Contentful Paint in milliseconds, or null if not yet observed.</param>
/// <param name="Ttfb">Time to First Byte in milliseconds, or null if not available.</param>
/// <param name="Cls">Cumulative Layout Shift score (unitless), or null if not yet observed.</param>
/// <param name="Inp">Interaction to Next Paint in milliseconds, or null if no interactions occurred.</param>
/// <param name="JsHeapSize">JavaScript heap used size in bytes, or null when unavailable (e.g. BiDi fallback).</param>
/// <param name="DomNodeCount">Number of DOM nodes, or null when unavailable (e.g. BiDi fallback).</param>
/// <param name="LayoutShifts">Individual layout shift entries observed during the page session.</param>
/// <param name="CollectedAtUtc">UTC timestamp when the metrics were collected.</param>
/// <param name="DiagnosticMessage">
/// Optional message when metrics could not be fully collected
/// (e.g. transport does not support CDP performance domains).
/// </param>
public sealed record PerformanceMetrics(
    double? Lcp,
    double? Fcp,
    double? Ttfb,
    double? Cls,
    double? Inp,
    long? JsHeapSize,
    int? DomNodeCount,
    IReadOnlyList<LayoutShiftEntry> LayoutShifts,
    DateTime CollectedAtUtc,
    string? DiagnosticMessage = null);
