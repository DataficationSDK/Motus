namespace Motus.Cli.Services.Reporters;

public sealed class ConsoleReporter(TextWriter writer) : ITestReporter
{
    public ConsoleReporter() : this(Console.Out) { }

    public Task OnRunStartedAsync(int total)
    {
        writer.WriteLine($"Running {total} test(s)...");
        writer.WriteLine();
        return Task.CompletedTask;
    }

    public Task OnTestCompletedAsync(TestResult result)
    {
        var status = result.Passed ? "PASS" : "FAIL";
        writer.WriteLine($"  [{status}] {result.FullName} ({result.Duration.TotalMilliseconds:F0}ms)");

        if (!result.Passed && result.ErrorMessage is not null)
        {
            writer.WriteLine($"         {result.ErrorMessage}");
        }

        return Task.CompletedTask;
    }

    public Task OnRunCompletedAsync(TestRunResult runResult)
    {
        writer.WriteLine();
        writer.WriteLine($"Results: {runResult.Passed} passed, {runResult.Failed} failed, {runResult.Total} total ({runResult.Duration.TotalSeconds:F1}s)");
        return Task.CompletedTask;
    }
}
