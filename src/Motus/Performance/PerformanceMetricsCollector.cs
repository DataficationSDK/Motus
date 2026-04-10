using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in plugin that collects Core Web Vitals and supplementary performance metrics
/// from the browser during test execution. Disabled by default; enabled via
/// <see cref="PerformanceOptions"/>.
/// </summary>
internal sealed class PerformanceMetricsCollector : IPlugin, ILifecycleHook
{
    private const string PerformanceObserverScript = """
        window.__motusPerf = { lcp: null, cls: 0, inp: null, layoutShifts: [], fcp: null };
        new PerformanceObserver((list) => {
          for (const entry of list.getEntries()) {
            window.__motusPerf.lcp = entry.startTime;
          }
        }).observe({ type: 'largest-contentful-paint', buffered: true });
        new PerformanceObserver((list) => {
          for (const entry of list.getEntries()) {
            if (!entry.hadRecentInput) {
              window.__motusPerf.cls += entry.value;
              window.__motusPerf.layoutShifts.push({
                score: entry.value,
                sources: (entry.sources || []).map(s => s.node ? s.node.nodeName : 'unknown')
              });
            }
          }
        }).observe({ type: 'layout-shift', buffered: true });
        new PerformanceObserver((list) => {
          for (const entry of list.getEntries()) {
            const dur = entry.processingEnd - entry.startTime;
            if (window.__motusPerf.inp === null || dur > window.__motusPerf.inp) {
              window.__motusPerf.inp = dur;
            }
          }
        }).observe({ type: 'event', buffered: true, durationThreshold: 16 });
        new PerformanceObserver((list) => {
          for (const entry of list.getEntries()) {
            if (entry.name === 'first-contentful-paint') {
              window.__motusPerf.fcp = entry.startTime;
            }
          }
        }).observe({ type: 'paint', buffered: true });
        """;

    private const string ReadMetricsScript = """
        JSON.stringify(window.__motusPerf || { lcp: null, cls: 0, inp: null, layoutShifts: [], fcp: null })
        """;

    private readonly PerformanceOptions _options;
    private BrowserContext? _context;

    internal PerformanceMetricsCollector(PerformanceOptions? options)
    {
        _options = options ?? new PerformanceOptions();
    }

    public string PluginId => "motus.performance-metrics";
    public string Name => "Performance Metrics Collector";
    public string Version => "1.0.0";
    public string? Author => "Motus";
    public string? Description => "Collects Core Web Vitals and performance metrics during test execution.";

    public Task OnLoadedAsync(IPluginContext context)
    {
        if (!_options.Enable)
            return Task.CompletedTask;

        _context = ((PluginContext)context).Context;
        context.RegisterLifecycleHook(this);
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task OnPageCreatedAsync(IPage page)
    {
        var concrete = (Page)page;
        var session = concrete.Session;

        if ((session.Capabilities & MotusCapabilities.PerformanceMetrics) == 0)
        {
            // BiDi fallback: inject PerformanceObserver script only (no CDP Performance domain).
            // JsHeapSize and DomNodeCount will be null.
            await session.SendAsync(
                "Page.addScriptToEvaluateOnNewDocument",
                new PageAddScriptToEvaluateOnNewDocumentParams(PerformanceObserverScript),
                CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentParams,
                CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentResult,
                CancellationToken.None).ConfigureAwait(false);
            return;
        }

        // Enable CDP Performance domain
        await session.SendAsync(
            "Performance.enable",
            CdpJsonContext.Default.PerformanceEnableResult,
            CancellationToken.None).ConfigureAwait(false);

        // Inject PerformanceObserver script for LCP, CLS, INP, and FCP
        await session.SendAsync(
            "Page.addScriptToEvaluateOnNewDocument",
            new PageAddScriptToEvaluateOnNewDocumentParams(PerformanceObserverScript),
            CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentParams,
            CdpJsonContext.Default.PageAddScriptToEvaluateOnNewDocumentResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    public Task BeforeNavigationAsync(IPage page, string url) => Task.CompletedTask;

    public async Task AfterNavigationAsync(IPage page, IResponse? response)
    {
        if (!_options.CollectAfterNavigation || _context is null)
            return;

        await CollectMetricsAsync(page).ConfigureAwait(false);
    }

    public Task BeforeActionAsync(IPage page, string action) => Task.CompletedTask;
    public Task AfterActionAsync(IPage page, string action, ActionResult result) => Task.CompletedTask;

    public async Task OnPageClosedAsync(IPage page)
    {
        if (_context is null)
            return;

        await CollectMetricsAsync(page).ConfigureAwait(false);
    }

    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message) => Task.CompletedTask;
    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error) => Task.CompletedTask;

    private async Task CollectMetricsAsync(IPage page)
    {
        var concrete = (Page)page;
        var session = concrete.Session;

        double? lcp = null, fcp = null, ttfb = null, cls = null, inp = null;
        long? jsHeapSize = null;
        int? domNodeCount = null;
        var layoutShifts = new List<LayoutShiftEntry>();
        string? diagnosticMessage = null;

        bool hasCdpPerformance = (session.Capabilities & MotusCapabilities.PerformanceMetrics) != 0;

        // Collect CDP Performance.getMetrics (TTFB, FCP fallback, heap, DOM nodes)
        if (hasCdpPerformance)
        {
            try
            {
                var metricsResult = await session.SendAsync(
                    "Performance.getMetrics",
                    CdpJsonContext.Default.PerformanceGetMetricsResult,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (var metric in metricsResult.Metrics)
                {
                    switch (metric.Name)
                    {
                        case "Timestamp":
                            // Timestamp is the navigation start epoch; TTFB is derived from
                            // the difference between NavigationStart and ResponseStart,
                            // but CDP Performance.getMetrics does not expose ResponseStart directly.
                            // We use the PerformanceObserver for more accurate timing.
                            break;
                        case "FirstContentfulPaint":
                            // CDP reports FCP as seconds since process start; only use as
                            // fallback if the PerformanceObserver did not capture it.
                            if (metric.Value > 0)
                                fcp = metric.Value * 1000; // convert to ms
                            break;
                        case "JSHeapUsedSize":
                            jsHeapSize = (long)metric.Value;
                            break;
                        case "Nodes":
                            domNodeCount = (int)metric.Value;
                            break;
                    }
                }
            }
            catch
            {
                // If metrics collection fails, continue with whatever we have
            }
        }
        else
        {
            diagnosticMessage = "CDP Performance domain is not supported on the active transport (" +
                                CapabilityGuard.GetTransportDescription(session) +
                                "). JsHeapSize and DomNodeCount are unavailable.";
        }

        // Read PerformanceObserver data from the page
        try
        {
            var evalResult = await session.SendAsync(
                "Runtime.evaluate",
                new RuntimeEvaluateParams(ReadMetricsScript, ReturnByValue: true),
                CdpJsonContext.Default.RuntimeEvaluateParams,
                CdpJsonContext.Default.RuntimeEvaluateResult,
                CancellationToken.None).ConfigureAwait(false);

            if (evalResult.ExceptionDetails is null && evalResult.Result.Value is { } jsonValue)
            {
                var json = jsonValue.GetString();
                if (json is not null)
                {
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("lcp", out var lcpEl) && lcpEl.ValueKind == JsonValueKind.Number)
                        lcp = lcpEl.GetDouble();

                    if (root.TryGetProperty("fcp", out var fcpEl) && fcpEl.ValueKind == JsonValueKind.Number)
                    {
                        // PerformanceObserver FCP is more accurate; prefer it over CDP
                        fcp = fcpEl.GetDouble();
                    }

                    if (root.TryGetProperty("cls", out var clsEl) && clsEl.ValueKind == JsonValueKind.Number)
                        cls = clsEl.GetDouble();

                    if (root.TryGetProperty("inp", out var inpEl) && inpEl.ValueKind == JsonValueKind.Number)
                        inp = inpEl.GetDouble();

                    // Extract TTFB from the navigation timing API
                    // (injected via PerformanceObserver doesn't cover TTFB, but we can get it from the page)

                    if (root.TryGetProperty("layoutShifts", out var shiftsEl) && shiftsEl.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var shiftEl in shiftsEl.EnumerateArray())
                        {
                            double score = 0;
                            var sources = new List<string>();

                            if (shiftEl.TryGetProperty("score", out var scoreEl) && scoreEl.ValueKind == JsonValueKind.Number)
                                score = scoreEl.GetDouble();

                            if (shiftEl.TryGetProperty("sources", out var sourcesEl) && sourcesEl.ValueKind == JsonValueKind.Array)
                            {
                                foreach (var sourceEl in sourcesEl.EnumerateArray())
                                {
                                    sources.Add(sourceEl.GetString() ?? "unknown");
                                }
                            }

                            layoutShifts.Add(new LayoutShiftEntry(score, sources));
                        }
                    }
                }
            }
        }
        catch
        {
            // If Runtime.evaluate fails (e.g. page already navigating), use what we have
        }

        // Collect TTFB via a separate Runtime.evaluate of the Navigation Timing API
        try
        {
            var ttfbResult = await session.SendAsync(
                "Runtime.evaluate",
                new RuntimeEvaluateParams(
                    "(() => { const t = performance.getEntriesByType('navigation')[0]; return t ? t.responseStart - t.startTime : null; })()",
                    ReturnByValue: true),
                CdpJsonContext.Default.RuntimeEvaluateParams,
                CdpJsonContext.Default.RuntimeEvaluateResult,
                CancellationToken.None).ConfigureAwait(false);

            if (ttfbResult.ExceptionDetails is null && ttfbResult.Result.Value is { } ttfbJson)
            {
                if (ttfbJson.ValueKind == JsonValueKind.Number)
                    ttfb = ttfbJson.GetDouble();
            }
        }
        catch
        {
            // TTFB collection is best-effort
        }

        var metrics = new PerformanceMetrics(
            Lcp: lcp,
            Fcp: fcp,
            Ttfb: ttfb,
            Cls: cls,
            Inp: inp,
            JsHeapSize: jsHeapSize,
            DomNodeCount: domNodeCount,
            LayoutShifts: layoutShifts,
            CollectedAtUtc: DateTime.UtcNow);

        concrete.LastPerformanceMetrics = metrics;
        PerformanceMetricsSink.Add(metrics);
    }
}
