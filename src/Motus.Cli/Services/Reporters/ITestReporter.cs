namespace Motus.Cli.Services.Reporters;

public interface ITestReporter
{
    Task OnRunStartedAsync(int total);
    Task OnTestCompletedAsync(TestResult result);
    Task OnRunCompletedAsync(TestRunResult runResult);
}
