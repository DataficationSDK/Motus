namespace Motus.Abstractions;

/// <summary>
/// Metadata for a test suite run, passed to the reporter when a run begins.
/// </summary>
/// <param name="SuiteName">The name of the test suite.</param>
/// <param name="TestCount">The total number of tests in the suite.</param>
/// <param name="Tags">Optional tags or labels associated with the suite.</param>
public sealed record TestSuiteInfo(
    string SuiteName,
    int TestCount,
    IReadOnlyList<string>? Tags = null);
