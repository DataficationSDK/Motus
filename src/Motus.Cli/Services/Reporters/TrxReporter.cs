using System.Xml.Linq;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class TrxReporter(string outputPath) : IReporter, IAccessibilityReporter, IPerformanceReporter
{
    private static readonly XNamespace TrxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private readonly List<(TestInfo Info, Abstractions.TestResult Result, Guid ExecutionId, Guid TestId)> _results = [];
    private readonly HashSet<string> _testsWithViolations = new();
    private readonly Dictionary<string, PerformanceMetrics> _perfMetrics = new();
    private readonly Guid _runId = Guid.NewGuid();

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnPerformanceMetricsCollectedAsync(PerformanceMetrics metrics, PerformanceBudgetResult? budgetResult, TestInfo test)
    {
        _perfMetrics[test.TestName] = metrics;
        return Task.CompletedTask;
    }

    public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
    {
        _testsWithViolations.Add(test.TestName);
        return Task.CompletedTask;
    }

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        _results.Add((test, result, Guid.NewGuid(), Guid.NewGuid()));
        return Task.CompletedTask;
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        var total = summary.Passed + summary.Failed + summary.Skipped;

        var results = new XElement(TrxNs + "Results");
        var definitions = new XElement(TrxNs + "TestDefinitions");
        var testEntries = new XElement(TrxNs + "TestEntries");

        foreach (var (info, result, executionId, testId) in _results)
        {
            var outcome = result.Passed ? "Passed" : "Failed";
            var duration = TimeSpan.FromMilliseconds(result.DurationMs);

            var unitTestResult = new XElement(TrxNs + "UnitTestResult",
                new XAttribute("executionId", executionId),
                new XAttribute("testId", testId),
                new XAttribute("testName", result.TestName),
                new XAttribute("duration", duration.ToString(@"hh\:mm\:ss\.fffffff")),
                new XAttribute("outcome", outcome));

            if (!result.Passed)
            {
                var output = new XElement(TrxNs + "Output");
                if (result.ErrorMessage is not null)
                {
                    output.Add(new XElement(TrxNs + "ErrorInfo",
                        new XElement(TrxNs + "Message", result.ErrorMessage),
                        result.StackTrace is not null
                            ? new XElement(TrxNs + "StackTrace", result.StackTrace)
                            : null));
                }
                unitTestResult.Add(output);
            }

            if (_perfMetrics.TryGetValue(result.TestName, out var pm))
            {
                var existingOutput = unitTestResult.Element(TrxNs + "Output");
                if (existingOutput is null)
                {
                    existingOutput = new XElement(TrxNs + "Output");
                    unitTestResult.Add(existingOutput);
                }

                var lines = new List<string> { "Performance Metrics:" };
                if (pm.Lcp is not null) lines.Add($"  LCP: {pm.Lcp:F1}ms");
                if (pm.Fcp is not null) lines.Add($"  FCP: {pm.Fcp:F1}ms");
                if (pm.Ttfb is not null) lines.Add($"  TTFB: {pm.Ttfb:F1}ms");
                if (pm.Cls is not null) lines.Add($"  CLS: {pm.Cls:F4}");
                if (pm.Inp is not null) lines.Add($"  INP: {pm.Inp:F1}ms");
                if (pm.JsHeapSize is not null) lines.Add($"  JSHeapSize: {pm.JsHeapSize}bytes");
                if (pm.DomNodeCount is not null) lines.Add($"  DOMNodeCount: {pm.DomNodeCount}");

                existingOutput.Add(new XElement(TrxNs + "StdOut", string.Join(Environment.NewLine, lines)));
            }

            results.Add(unitTestResult);

            // Extract class name and method name from fully qualified test name
            var lastDot = result.TestName.LastIndexOf('.');
            var className = lastDot > 0 ? result.TestName[..lastDot] : info.SuiteName;
            var methodName = lastDot > 0 ? result.TestName[(lastDot + 1)..] : result.TestName;

            var unitTest = new XElement(TrxNs + "UnitTest",
                new XAttribute("name", result.TestName),
                new XAttribute("id", testId),
                new XElement(TrxNs + "Execution",
                    new XAttribute("id", executionId)),
                new XElement(TrxNs + "TestMethod",
                    new XAttribute("codeBase", info.SuiteName),
                    new XAttribute("className", className),
                    new XAttribute("name", methodName)));

            if (_testsWithViolations.Contains(result.TestName))
            {
                unitTest.Add(new XElement(TrxNs + "TestCategory",
                    new XElement(TrxNs + "TestCategoryItem",
                        new XAttribute("TestCategory", "a11y"))));
            }

            definitions.Add(unitTest);

            testEntries.Add(new XElement(TrxNs + "TestEntry",
                new XAttribute("testId", testId),
                new XAttribute("executionId", executionId)));
        }

        var resultSummary = new XElement(TrxNs + "ResultSummary",
            new XAttribute("outcome", summary.Failed > 0 ? "Failed" : "Completed"),
            new XElement(TrxNs + "Counters",
                new XAttribute("total", total),
                new XAttribute("executed", summary.Passed + summary.Failed),
                new XAttribute("passed", summary.Passed),
                new XAttribute("failed", summary.Failed),
                new XAttribute("notExecuted", summary.Skipped)));

        var testRun = new XElement(TrxNs + "TestRun",
            new XAttribute("id", _runId),
            new XAttribute("name", summary.SuiteName),
            results,
            definitions,
            testEntries,
            resultSummary);

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), testRun);
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, doc.Declaration + Environment.NewLine + doc.Root);
        Console.WriteLine($"TRX report written to {outputPath}");
    }
}
