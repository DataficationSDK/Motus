using System.Xml.Linq;

namespace Motus.Cli.Services.Reporters;

public sealed class JUnitReporter(string outputPath) : ITestReporter
{
    private readonly List<TestResult> _results = [];

    public Task OnRunStartedAsync(int total) => Task.CompletedTask;

    public Task OnTestCompletedAsync(TestResult result)
    {
        _results.Add(result);
        return Task.CompletedTask;
    }

    public async Task OnRunCompletedAsync(TestRunResult runResult)
    {
        var testSuite = new XElement("testsuite",
            new XAttribute("name", "Motus Tests"),
            new XAttribute("tests", runResult.Total),
            new XAttribute("failures", runResult.Failed),
            new XAttribute("time", runResult.Duration.TotalSeconds.ToString("F3")));

        foreach (var result in _results)
        {
            var testCase = new XElement("testcase",
                new XAttribute("name", result.FullName),
                new XAttribute("time", result.Duration.TotalSeconds.ToString("F3")));

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
