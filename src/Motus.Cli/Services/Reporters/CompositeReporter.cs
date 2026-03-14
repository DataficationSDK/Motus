using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

public sealed class CompositeReporter(IReadOnlyList<IReporter> reporters) : IReporter
{
    public async Task OnTestRunStartAsync(TestSuiteInfo suite)
    {
        foreach (var reporter in reporters)
        {
            try { await reporter.OnTestRunStartAsync(suite); }
            catch { }
        }
    }

    public async Task OnTestStartAsync(TestInfo test)
    {
        foreach (var reporter in reporters)
        {
            try { await reporter.OnTestStartAsync(test); }
            catch { }
        }
    }

    public async Task OnTestEndAsync(TestInfo test, Abstractions.TestResult result)
    {
        foreach (var reporter in reporters)
        {
            try { await reporter.OnTestEndAsync(test, result); }
            catch { }
        }
    }

    public async Task OnTestRunEndAsync(TestRunSummary summary)
    {
        foreach (var reporter in reporters)
        {
            try { await reporter.OnTestRunEndAsync(summary); }
            catch { }
        }
    }
}
