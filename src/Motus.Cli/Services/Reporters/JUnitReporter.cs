using System.Xml.Linq;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class JUnitReporter(string outputPath) : IReporter, IAccessibilityReporter, IPerformanceReporter
{
    private readonly List<(TestInfo Info, Abstractions.TestResult Result)> _results = [];
    private readonly Dictionary<string, List<AccessibilityViolation>> _violations = new();
    private readonly Dictionary<string, PerformanceMetrics> _perfMetrics = new();

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnPerformanceMetricsCollectedAsync(PerformanceMetrics metrics, PerformanceBudgetResult? budgetResult, TestInfo test)
    {
        _perfMetrics[test.TestName] = metrics;
        return Task.CompletedTask;
    }

    public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
    {
        if (!_violations.TryGetValue(test.TestName, out var list))
        {
            list = [];
            _violations[test.TestName] = list;
        }
        list.Add(violation);
        return Task.CompletedTask;
    }

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        _results.Add((test, result));
        return Task.CompletedTask;
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        var total = summary.Passed + summary.Failed + summary.Skipped;
        var testSuite = new XElement("testsuite",
            new XAttribute("name", summary.SuiteName),
            new XAttribute("tests", total),
            new XAttribute("failures", summary.Failed),
            new XAttribute("skipped", summary.Skipped),
            new XAttribute("time", (summary.TotalDurationMs / 1000).ToString("F3")));

        foreach (var (info, result) in _results)
        {
            var testCase = new XElement("testcase",
                new XAttribute("name", result.TestName),
                new XAttribute("classname", info.SuiteName),
                new XAttribute("time", (result.DurationMs / 1000).ToString("F3")));

            if (!result.Passed)
            {
                testCase.Add(new XElement("failure",
                    new XAttribute("message", result.ErrorMessage ?? ""),
                    result.StackTrace ?? ""));
            }

            if (_violations.TryGetValue(result.TestName, out var violations) && violations.Count > 0)
            {
                var details = string.Join("\n", violations.Select(v =>
                {
                    var selector = v.Selector is not null ? $" ({v.Selector})" : "";
                    return $"[{v.Severity}] {v.RuleId}: {v.Message}{selector}";
                }));
                testCase.Add(new XElement("failure",
                    new XAttribute("type", "accessibility"),
                    new XAttribute("message", $"{violations.Count} accessibility violation(s)"),
                    details));
            }

            if (_perfMetrics.TryGetValue(result.TestName, out var pm))
            {
                var props = new XElement("properties");
                if (pm.Lcp is not null) props.Add(new XElement("property", new XAttribute("name", "perf.lcp"), new XAttribute("value", pm.Lcp.Value.ToString("F1"))));
                if (pm.Fcp is not null) props.Add(new XElement("property", new XAttribute("name", "perf.fcp"), new XAttribute("value", pm.Fcp.Value.ToString("F1"))));
                if (pm.Ttfb is not null) props.Add(new XElement("property", new XAttribute("name", "perf.ttfb"), new XAttribute("value", pm.Ttfb.Value.ToString("F1"))));
                if (pm.Cls is not null) props.Add(new XElement("property", new XAttribute("name", "perf.cls"), new XAttribute("value", pm.Cls.Value.ToString("F4"))));
                if (pm.Inp is not null) props.Add(new XElement("property", new XAttribute("name", "perf.inp"), new XAttribute("value", pm.Inp.Value.ToString("F1"))));
                if (pm.JsHeapSize is not null) props.Add(new XElement("property", new XAttribute("name", "perf.jsHeapSize"), new XAttribute("value", pm.JsHeapSize.Value.ToString())));
                if (pm.DomNodeCount is not null) props.Add(new XElement("property", new XAttribute("name", "perf.domNodeCount"), new XAttribute("value", pm.DomNodeCount.Value.ToString())));
                if (props.HasElements)
                    testCase.Add(props);
            }

            if (result.Attachments is { Count: > 0 })
            {
                testCase.Add(new XElement("system-out",
                    string.Join("\n", result.Attachments)));
            }

            testSuite.Add(testCase);
        }

        var doc = new XDocument(new XElement("testsuites", testSuite));
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, doc.ToString());
        Console.WriteLine($"JUnit report written to {outputPath}");
    }
}
