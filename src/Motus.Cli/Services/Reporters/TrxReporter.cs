using System.Xml.Linq;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class TrxReporter(string outputPath) : IReporter
{
    private static readonly XNamespace TrxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private readonly List<(TestInfo Info, Abstractions.TestResult Result, Guid ExecutionId, Guid TestId)> _results = [];
    private readonly Guid _runId = Guid.NewGuid();

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

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

            results.Add(unitTestResult);

            // Extract class name and method name from fully qualified test name
            var lastDot = result.TestName.LastIndexOf('.');
            var className = lastDot > 0 ? result.TestName[..lastDot] : info.SuiteName;
            var methodName = lastDot > 0 ? result.TestName[(lastDot + 1)..] : result.TestName;

            definitions.Add(new XElement(TrxNs + "UnitTest",
                new XAttribute("name", result.TestName),
                new XAttribute("id", testId),
                new XElement(TrxNs + "Execution",
                    new XAttribute("id", executionId)),
                new XElement(TrxNs + "TestMethod",
                    new XAttribute("codeBase", info.SuiteName),
                    new XAttribute("className", className),
                    new XAttribute("name", methodName))));

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
