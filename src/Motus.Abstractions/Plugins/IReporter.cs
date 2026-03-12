namespace Motus.Abstractions;

/// <summary>
/// Captures and reports test execution results. Multiple reporters can be active simultaneously.
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Called before any tests execute. Receives the full test suite metadata.
    /// </summary>
    /// <param name="suite">Metadata for the test suite that is about to run.</param>
    Task OnTestRunStartAsync(TestSuiteInfo suite);

    /// <summary>
    /// Called before each individual test executes.
    /// </summary>
    /// <param name="test">Metadata for the test that is about to run.</param>
    Task OnTestStartAsync(TestInfo test);

    /// <summary>
    /// Called after each test completes with pass/fail status, duration, error details, and attachments.
    /// </summary>
    /// <param name="test">Metadata for the test that just ended.</param>
    /// <param name="result">The outcome including duration, errors, and attachments.</param>
    Task OnTestEndAsync(TestInfo test, TestResult result);

    /// <summary>
    /// Called after all tests have executed. Receives aggregate results.
    /// </summary>
    /// <param name="summary">Aggregate results for the completed run.</param>
    Task OnTestRunEndAsync(TestRunSummary summary);
}
