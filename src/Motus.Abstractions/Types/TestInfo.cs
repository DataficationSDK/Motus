namespace Motus.Abstractions;

/// <summary>
/// Metadata for an individual test, passed to the reporter when a test begins.
/// </summary>
/// <param name="TestName">The fully qualified name of the test.</param>
/// <param name="SuiteName">The name of the suite this test belongs to.</param>
/// <param name="Tags">Optional tags or labels associated with the test.</param>
public sealed record TestInfo(
    string TestName,
    string SuiteName,
    IReadOnlyList<string>? Tags = null);
