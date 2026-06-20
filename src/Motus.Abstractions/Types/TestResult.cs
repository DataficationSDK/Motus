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
/// <param name="Flaky">Whether the test failed at least once and then passed within its retry budget.</param>
/// <param name="Quarantined">Whether the test is quarantined, so its outcome does not gate the run.</param>
/// <param name="Attempts">The number of times the test was executed (1 when it passed or failed on the first try).</param>
public sealed record TestResult(
    string TestName,
    bool Passed,
    double DurationMs,
    string? ErrorMessage = null,
    string? StackTrace = null,
    IReadOnlyList<string>? Attachments = null,
    bool Flaky = false,
    bool Quarantined = false,
    int Attempts = 1);
