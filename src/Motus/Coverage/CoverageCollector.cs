using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in plugin that collects JavaScript and CSS code coverage from the browser
/// during test execution via the CDP Profiler and CSS rule-usage domains.
/// Disabled by default; enabled via <see cref="CoverageOptions"/>.
/// </summary>
internal sealed class CoverageCollector : IPlugin, ILifecycleHook
{
    private readonly CoverageOptions _options;
    private BrowserContext? _context;
    private IMotusLogger? _logger;

    internal CoverageCollector(CoverageOptions? options)
    {
        _options = options ?? new CoverageOptions();
    }

    public string PluginId => "motus.code-coverage";
    public string Name => "Code Coverage Collector";
    public string Version => "1.0.0";
    public string? Author => "Motus";
    public string? Description => "Collects JavaScript and CSS code coverage during test execution.";

    public Task OnLoadedAsync(IPluginContext context)
    {
        if (!_options.Enable)
            return Task.CompletedTask;

        _context = ((PluginContext)context).Context;
        _logger = context.CreateLogger("motus.coverage");
        context.RegisterLifecycleHook(this);
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;

    public async Task OnPageCreatedAsync(IPage page)
    {
        var concrete = (Page)page;
        var session = concrete.Session;

        if ((session.Capabilities & MotusCapabilities.CodeCoverage) == 0)
        {
            _logger?.LogWarning(
                "Code coverage requires CDP. The active transport (" +
                CapabilityGuard.GetTransportDescription(session) +
                ") does not support it; coverage data will be empty.");
            return;
        }

        if (_options.IncludeJavaScript)
        {
            try
            {
                await session.SendAsync(
                    "Profiler.enable",
                    CdpJsonContext.Default.ProfilerEnableResult,
                    CancellationToken.None).ConfigureAwait(false);

                await session.SendAsync(
                    "Profiler.startPreciseCoverage",
                    new ProfilerStartPreciseCoverageParams(
                        CallCount: true,
                        Detailed: true,
                        AllowTriggeredUpdates: false),
                    CdpJsonContext.Default.ProfilerStartPreciseCoverageParams,
                    CdpJsonContext.Default.ProfilerStartPreciseCoverageResult,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError("Failed to start JavaScript coverage collection.", ex);
            }
        }

        if (_options.IncludeCss)
        {
            try
            {
                await session.SendAsync(
                    "CSS.enable",
                    CdpJsonContext.Default.CssEnableResult,
                    CancellationToken.None).ConfigureAwait(false);

                await session.SendAsync(
                    "CSS.startRuleUsageTracking",
                    CdpJsonContext.Default.CssStartRuleUsageTrackingResult,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError("Failed to start CSS rule usage tracking.", ex);
            }
        }
    }

    public Task BeforeNavigationAsync(IPage page, string url) => Task.CompletedTask;
    public Task AfterNavigationAsync(IPage page, IResponse? response) => Task.CompletedTask;
    public Task BeforeActionAsync(IPage page, string action) => Task.CompletedTask;
    public Task AfterActionAsync(IPage page, string action, ActionResult result) => Task.CompletedTask;
    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message) => Task.CompletedTask;
    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error) => Task.CompletedTask;

    public async Task OnPageClosedAsync(IPage page)
    {
        if (_context is null)
            return;

        var concrete = (Page)page;
        var session = concrete.Session;

        if ((session.Capabilities & MotusCapabilities.CodeCoverage) == 0)
        {
            var empty = new CoverageData(
                Scripts: Array.Empty<ScriptCoverage>(),
                Stylesheets: Array.Empty<StylesheetCoverage>(),
                Summary: new CoverageSummary(0, 0, 0, 0, 0, 0),
                CollectedAtUtc: DateTime.UtcNow,
                DiagnosticMessage: "Code coverage requires CDP. The active transport (" +
                                   CapabilityGuard.GetTransportDescription(session) +
                                   ") does not support it.");
            concrete.LastCoverage = empty;
            CoverageSink.Add(empty);
            return;
        }

        var scripts = new List<ScriptCoverage>();
        var stylesheets = new List<StylesheetCoverage>();

        if (_options.IncludeJavaScript)
        {
            try
            {
                var jsResult = await session.SendAsync(
                    "Profiler.takePreciseCoverage",
                    CdpJsonContext.Default.ProfilerTakePreciseCoverageResult,
                    CancellationToken.None).ConfigureAwait(false);

                foreach (var s in jsResult.Result)
                {
                    string source = string.Empty;
                    try
                    {
                        var src = await session.SendAsync(
                            "Profiler.getScriptSource",
                            new ProfilerGetScriptSourceParams(s.ScriptId),
                            CdpJsonContext.Default.ProfilerGetScriptSourceParams,
                            CdpJsonContext.Default.ProfilerGetScriptSourceResult,
                            CancellationToken.None).ConfigureAwait(false);
                        source = src.ScriptSource ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Failed to fetch source for script {s.ScriptId} ({s.Url}).", ex);
                    }

                    var ranges = new List<CoverageRange>();
                    foreach (var fn in s.Functions)
                    {
                        foreach (var r in fn.Ranges)
                            ranges.Add(new CoverageRange(r.StartOffset, r.EndOffset, r.Count));
                    }

                    var url = string.IsNullOrEmpty(s.Url) ? s.ScriptId : s.Url;
                    var stats = CoverageAggregator.SummarizeScript(source, ranges);
                    scripts.Add(new ScriptCoverage(url, source, ranges, stats));
                }

                try
                {
                    await session.SendAsync(
                        "Profiler.stopPreciseCoverage",
                        CdpJsonContext.Default.ProfilerStopPreciseCoverageResult,
                        CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort: stopping the profiler is not critical
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Failed to collect JavaScript coverage.", ex);
            }
        }

        if (_options.IncludeCss)
        {
            try
            {
                var cssResult = await session.SendAsync(
                    "CSS.stopRuleUsageTracking",
                    CdpJsonContext.Default.CssStopRuleUsageTrackingResult,
                    CancellationToken.None).ConfigureAwait(false);

                var bySheet = new Dictionary<string, List<CssRuleUsageEntry>>(StringComparer.Ordinal);
                foreach (var rule in cssResult.RuleUsage)
                {
                    if (!bySheet.TryGetValue(rule.StyleSheetId, out var list))
                    {
                        list = new List<CssRuleUsageEntry>();
                        bySheet[rule.StyleSheetId] = list;
                    }
                    list.Add(rule);
                }

                foreach (var (sheetId, rules) in bySheet)
                {
                    string source = string.Empty;
                    try
                    {
                        var text = await session.SendAsync(
                            "CSS.getStyleSheetText",
                            new CssGetStyleSheetTextParams(sheetId),
                            CdpJsonContext.Default.CssGetStyleSheetTextParams,
                            CdpJsonContext.Default.CssGetStyleSheetTextResult,
                            CancellationToken.None).ConfigureAwait(false);
                        source = text.Text ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError($"Failed to fetch source for stylesheet {sheetId}.", ex);
                    }

                    var usages = rules
                        .Select(r => new CssRuleUsage(r.StartOffset, r.EndOffset, r.Used))
                        .ToList();
                    var stats = CoverageAggregator.SummarizeStylesheet(usages);
                    // CSS.styleSheetAdded events are not consumed in this phase; the
                    // stylesheet ID is used as a URL identifier.
                    stylesheets.Add(new StylesheetCoverage(sheetId, source, usages, stats));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError("Failed to collect CSS coverage.", ex);
            }
        }

        var summary = CoverageAggregator.BuildSummary(scripts, stylesheets);
        var data = new CoverageData(
            Scripts: scripts,
            Stylesheets: stylesheets,
            Summary: summary,
            CollectedAtUtc: DateTime.UtcNow);

        concrete.LastCoverage = data;
        CoverageSink.Add(data);
    }
}
