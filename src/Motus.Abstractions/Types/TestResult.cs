namespace Motus.Abstractions;

/// <summary>
/// The outcome of an individual test, passed to the reporter when a test ends.
/// </summary>
/// <param name="TestName">The fully qualified name of the test.</param>
/// <param name="Passed">Whether the test passed.</param>
/// <param name="DurationMs">The duration of the test in milliseconds.</param>
/// <param name="ErrorMessage">The error message if the test failed, or null.</param>
/// <param name="StackTrace">The stack trace if the test failed, or null.</param>
/// <param name="Attachments">File paths to screenshots, traces, or other artifacts captured during the test.</param>
public sealed record TestResult(
    string TestName,
    bool Passed,
    double DurationMs,
    string? ErrorMessage = null,
    string? StackTrace = null,
    IReadOnlyList<string>? Attachments = null);
