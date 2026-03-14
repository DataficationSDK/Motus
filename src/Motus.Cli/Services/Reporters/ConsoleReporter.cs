using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class ConsoleReporter(TextWriter writer) : IReporter
{
    public ConsoleReporter() : this(Console.Out) { }

    public Task OnTestRunStartAsync(TestSuiteInfo suite)
    {
        writer.WriteLine($"Running {suite.TestCount} test(s)...");
        writer.WriteLine();
        return Task.CompletedTask;
    }

    public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

    public Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        var status = result.Passed ? "PASS" : "FAIL";
        writer.WriteLine($"  [{status}] {result.TestName} ({result.DurationMs:F0}ms)");

        if (!result.Passed && result.ErrorMessage is not null)
        {
            writer.WriteLine($"         {result.ErrorMessage}");
        }

        return Task.CompletedTask;
    }

    public Task OnTestRunEndAsync(TestRunSummary summary)
    {
        writer.WriteLine();
        var total = summary.Passed + summary.Failed + summary.Skipped;
        writer.WriteLine($"Results: {summary.Passed} passed, {summary.Failed} failed, {total} total ({summary.TotalDurationMs / 1000:F1}s)");
        return Task.CompletedTask;
    }
}
