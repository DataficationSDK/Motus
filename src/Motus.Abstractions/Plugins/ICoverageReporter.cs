namespace Motus.Abstractions;

/// <summary>
/// Opt-in reporter interface for receiving JavaScript and CSS code coverage data
/// collected during test execution. Reporters implement both <see cref="IReporter"/>
/// and <see cref="ICoverageReporter"/> to receive coverage events. Separate from
/// <see cref="IReporter"/> to avoid default interface methods, which conflict with
/// NativeAOT trimming.
/// </summary>
public interface ICoverageReporter
{
    /// <summary>
    /// Called when coverage data has been collected for a single test.
    /// </summary>
    /// <param name="coverage">The collected coverage snapshot for this test.</param>
    /// <param name="test">The test that produced the snapshot.</param>
    Task OnCoverageCollectedAsync(CoverageData coverage, TestInfo test);

    /// <summary>
    /// Called once at the end of the test run with the aggregated coverage data
    /// merged across all tests. This is the hook for run-level reports
    /// (console summary, HTML report, Cobertura XML).
    /// </summary>
    /// <param name="aggregated">Coverage data merged across the entire run.</param>
    Task OnCoverageRunEndAsync(CoverageData aggregated);
}
