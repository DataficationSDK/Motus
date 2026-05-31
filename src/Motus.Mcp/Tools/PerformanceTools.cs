using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// Reports performance telemetry for the active page: Core Web Vitals plus heap
/// size and DOM node count, captured after the most recent navigation.
/// </summary>
[McpServerToolType]
public sealed class PerformanceTools
{
    [McpServerTool(Name = "get_performance", Title = "Get performance metrics", Destructive = false, ReadOnly = true)]
    [Description("Returns performance telemetry for the active page: the Core Web Vitals "
        + "LCP, FCP, TTFB, and INP in milliseconds, CLS as a unitless layout-shift score, "
        + "the JS heap size in bytes, and the DOM node count. Metrics reflect the most recent "
        + "navigation, with the latest vitals re-read on each call.")]
    public static async Task<CallToolResult> GetPerformanceAsync(
        ActivePageService pageService,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await pageService.GetOrCreateActivePageAsync(cancellationToken).ConfigureAwait(false);
            var metrics = await page.GetPerformanceMetricsAsync(cancellationToken).ConfigureAwait(false);

            if (metrics is null)
                return ToolResultHelper.Text(
                    "No performance metrics available yet. Navigate to a page first, then retry.");

            var payload = new JsonObject
            {
                ["lcp"] = metrics.Lcp,
                ["fcp"] = metrics.Fcp,
                ["ttfb"] = metrics.Ttfb,
                ["cls"] = metrics.Cls,
                ["inp"] = metrics.Inp,
                ["jsHeapSize"] = metrics.JsHeapSize,
                ["domNodeCount"] = metrics.DomNodeCount,
                ["layoutShiftCount"] = metrics.LayoutShifts.Count,
                ["collectedAtUtc"] = metrics.CollectedAtUtc.ToString("o"),
            };
            if (!string.IsNullOrEmpty(metrics.DiagnosticMessage))
                payload["diagnosticMessage"] = metrics.DiagnosticMessage;

            // Build the structured value through the node API and parse it, rather than
            // reflection-serializing a type, so the trim and AOT analyzers stay satisfied.
            var element = JsonDocument.Parse(payload.ToJsonString()).RootElement.Clone();
            return ToolResultHelper.Structured(element, Summarize(metrics));
        }
        catch (Exception ex)
        {
            return ToolResultHelper.Error($"Reading performance metrics failed: {ex.Message}");
        }
    }

    private static string Summarize(PerformanceMetrics metrics)
    {
        var summary = $"LCP {Ms(metrics.Lcp)}, FCP {Ms(metrics.Fcp)}, TTFB {Ms(metrics.Ttfb)}, "
            + $"CLS {Score(metrics.Cls)}, INP {Ms(metrics.Inp)}.";
        return string.IsNullOrEmpty(metrics.DiagnosticMessage)
            ? summary
            : $"{summary} {metrics.DiagnosticMessage}";
    }

    private static string Ms(double? value)
        => value is { } v ? $"{v:0}ms" : "n/a";

    private static string Score(double? value)
        => value is { } v ? v.ToString("0.###") : "n/a";
}
