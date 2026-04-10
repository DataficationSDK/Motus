namespace Motus.Abstractions;

/// <summary>
/// Opt-in reporter interface for receiving performance metrics collected during test execution.
/// Reporters implement both <see cref="IReporter"/> and <see cref="IPerformanceReporter"/>
/// to receive performance events. Separate from <see cref="IReporter"/> to avoid default
/// interface methods, which conflict with NativeAOT trimming.
/// </summary>
public interface IPerformanceReporter
{
    /// <summary>
    /// Called when performance metrics have been collected for a test.
    /// </summary>
    /// <param name="metrics">The collected performance metrics.</param>
    /// <param name="budgetResult">The budget evaluation result, or null if no budget is configured.</param>
    /// <param name="test">The test that produced the metrics.</param>
    Task OnPerformanceMetricsCollectedAsync(PerformanceMetrics metrics, PerformanceBudgetResult? budgetResult, TestInfo test);
}
