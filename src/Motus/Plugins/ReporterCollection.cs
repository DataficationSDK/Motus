using Motus.Abstractions;

namespace Motus;

public sealed class ReporterCollection
{
    private readonly List<IReporter> _reporters = [];

    internal void Add(IReporter reporter)
    {
        lock (_reporters)
            _reporters.Add(reporter);
    }

    private IReporter[] Snapshot()
    {
        lock (_reporters)
            return [.. _reporters];
    }

    public async Task FireOnTestRunStartAsync(TestSuiteInfo suite)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestRunStartAsync(suite).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task FireOnTestStartAsync(TestInfo test)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestStartAsync(test).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task FireOnTestEndAsync(TestInfo test, TestResult result)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestEndAsync(test, result).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task FireOnTestRunEndAsync(TestRunSummary summary)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestRunEndAsync(summary).ConfigureAwait(false); }
            catch { }
        }
    }

    public async Task FireOnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
    {
        foreach (var reporter in Snapshot())
        {
            if (reporter is IAccessibilityReporter a11y)
            {
                try { await a11y.OnAccessibilityViolationAsync(violation, test).ConfigureAwait(false); }
                catch { }
            }
        }
    }
}
