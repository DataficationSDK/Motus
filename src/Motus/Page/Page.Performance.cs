using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    private const string ReadPerfScript = """
        JSON.stringify(window.__motusPerf || { lcp: null, cls: 0, inp: null, layoutShifts: [], fcp: null })
        """;

    /// <summary>
    /// The most recent performance metrics, set by <see cref="PerformanceMetricsCollector"/>
    /// after navigation or page close. Null when the hook is disabled or no collection has run.
    /// </summary>
    internal PerformanceMetrics? LastPerformanceMetrics { get; set; }

    /// <summary>
    /// The active performance budget for this page, set by test framework adapters
    /// from <see cref="PerformanceBudgetAttribute"/> resolution.
    /// </summary>
    internal PerformanceBudget? ActivePerformanceBudget { get; set; }

    /// <summary>
    /// Re-reads the PerformanceObserver data from the page and updates
    /// <see cref="LastPerformanceMetrics"/>. Called by assertions during retry
    /// to pick up metrics that arrived after the initial post-navigation collection.
    /// </summary>
    internal async Task RefreshPerformanceMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            var evalResult = await _session.SendAsync(
                "Runtime.evaluate",
                new RuntimeEvaluateParams(ReadPerfScript, ReturnByValue: true),
                CdpJsonContext.Default.RuntimeEvaluateParams,
                CdpJsonContext.Default.RuntimeEvaluateResult,
                ct).ConfigureAwait(false);

            if (evalResult.ExceptionDetails is not null || evalResult.Result.Value is not { } jsonValue)
                return;

            var json = jsonValue.GetString();
            if (json is null)
                return;

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var existing = LastPerformanceMetrics;

            double? lcp = existing?.Lcp, fcp = existing?.Fcp, cls = existing?.Cls, inp = existing?.Inp;

            if (root.TryGetProperty("lcp", out var lcpEl) && lcpEl.ValueKind == JsonValueKind.Number)
                lcp = lcpEl.GetDouble();
            if (root.TryGetProperty("fcp", out var fcpEl) && fcpEl.ValueKind == JsonValueKind.Number)
                fcp = fcpEl.GetDouble();
            if (root.TryGetProperty("cls", out var clsEl) && clsEl.ValueKind == JsonValueKind.Number)
                cls = clsEl.GetDouble();
            if (root.TryGetProperty("inp", out var inpEl) && inpEl.ValueKind == JsonValueKind.Number)
                inp = inpEl.GetDouble();

            LastPerformanceMetrics = new PerformanceMetrics(
                Lcp: lcp,
                Fcp: fcp,
                Ttfb: existing?.Ttfb,
                Cls: cls,
                Inp: inp,
                JsHeapSize: existing?.JsHeapSize,
                DomNodeCount: existing?.DomNodeCount,
                LayoutShifts: existing?.LayoutShifts ?? [],
                CollectedAtUtc: DateTime.UtcNow);
        }
        catch
        {
            // Best effort; keep existing metrics
        }
    }
}
