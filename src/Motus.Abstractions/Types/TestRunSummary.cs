namespace Motus.Abstractions;

/// <summary>
/// Aggregate results for a completed test run, passed to the reporter at the end of a run.
/// </summary>
/// <param name="SuiteName">The name of the test suite.</param>
/// <param name="Passed">The number of tests that passed.</param>
/// <param name="Failed">The number of tests that failed.</param>
/// <param name="Skipped">The number of tests that were skipped.</param>
/// <param name="TotalDurationMs">The total wall-clock duration of the run in milliseconds.</param>
public sealed record TestRunSummary(
    string SuiteName,
    int Passed,
    int Failed,
    int Skipped,
    double TotalDurationMs);
