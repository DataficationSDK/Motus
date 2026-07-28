using Motus.Abstractions;

namespace Motus;

/// <summary>
/// The reporters registered on a browser context, and the fan-out that delivers each lifecycle
/// event to all of them.
/// </summary>
/// <remarks>
/// A reporter that throws is ignored rather than allowed to propagate. Reporting is an observer of
/// the run and must not be able to fail it, and one misbehaving reporter must not stop the others
/// from being told. The consequence is worth knowing while writing one: a reporter that throws
/// fails silently, so handle and surface your own errors.
/// </remarks>
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

    /// <summary>Tells every reporter that a test run is beginning.</summary>
    /// <param name="suite">The suite about to run.</param>
    public async Task FireOnTestRunStartAsync(TestSuiteInfo suite)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestRunStartAsync(suite).ConfigureAwait(false); }
            catch { }
        }
    }

    /// <summary>Tells every reporter that a single test is beginning.</summary>
    /// <param name="test">The test about to run.</param>
    public async Task FireOnTestStartAsync(TestInfo test)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestStartAsync(test).ConfigureAwait(false); }
            catch { }
        }
    }

    /// <summary>Tells every reporter that a single test has finished, and how it went.</summary>
    /// <param name="test">The test that ran.</param>
    /// <param name="result">Its outcome.</param>
    public async Task FireOnTestEndAsync(TestInfo test, TestResult result)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestEndAsync(test, result).ConfigureAwait(false); }
            catch { }
        }
    }

    /// <summary>Tells every reporter that the run has finished, and how it went overall.</summary>
    /// <param name="summary">The totals for the run.</param>
    public async Task FireOnTestRunEndAsync(TestRunSummary summary)
    {
        foreach (var reporter in Snapshot())
        {
            try { await reporter.OnTestRunEndAsync(summary).ConfigureAwait(false); }
            catch { }
        }
    }

    /// <summary>
    /// Tells the reporters that also implement <see cref="IAccessibilityReporter"/> about an
    /// accessibility violation; the rest are skipped.
    /// </summary>
    /// <param name="violation">The violation found.</param>
    /// <param name="test">The test during which it was found.</param>
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

    /// <summary>
    /// Tells the reporters that also implement <see cref="IPerformanceReporter"/> about a set of
    /// collected metrics; the rest are skipped.
    /// </summary>
    /// <param name="metrics">The metrics collected from the page.</param>
    /// <param name="budgetResult">How they measured against the active budget, if one was in effect.</param>
    /// <param name="test">The test during which they were collected.</param>
    public async Task FireOnPerformanceMetricsCollectedAsync(PerformanceMetrics metrics, PerformanceBudgetResult? budgetResult, TestInfo test)
    {
        foreach (var reporter in Snapshot())
        {
            if (reporter is IPerformanceReporter perf)
            {
                try { await perf.OnPerformanceMetricsCollectedAsync(metrics, budgetResult, test).ConfigureAwait(false); }
                catch { }
            }
        }
    }
}
