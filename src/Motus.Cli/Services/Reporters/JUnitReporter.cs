using System.Xml.Linq;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class JUnitReporter(string outputPath) : IReporter
{
    private readonly List<Abstractions.TestResult> _results = [];

    public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        _results.Add(result);
        return Task.CompletedTask;
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        var total = summary.Passed + summary.Failed + summary.Skipped;
        var testSuite = new XElement("testsuite",
            new XAttribute("name", summary.SuiteName),
            new XAttribute("tests", total),
            new XAttribute("failures", summary.Failed),
            new XAttribute("time", (summary.TotalDurationMs / 1000).ToString("F3")));

        foreach (var result in _results)
        {
            var testCase = new XElement("testcase",
                new XAttribute("name", result.TestName),
                new XAttribute("time", (result.DurationMs / 1000).ToString("F3")));

            if (!result.Passed)
            {
                testCase.Add(new XElement("failure",
                    new XAttribute("message", result.ErrorMessage ?? ""),
                    result.StackTrace ?? ""));
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
