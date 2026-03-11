namespace Motus.Abstractions;

/// <summary>
/// Receives notifications about test execution progress for custom reporting.
/// </summary>
public interface IReporter
{
    /// <summary>
    /// Called when a test run begins.
    /// </summary>
    /// <param name="testCount">The total number of tests to run.</param>
    Task OnTestRunStartAsync(int testCount);

    /// <summary>
    /// Called when a test run ends.
    /// </summary>
    /// <param name="passed">The number of tests that passed.</param>
    /// <param name="failed">The number of tests that failed.</param>
    /// <param name="skipped">The number of tests that were skipped.</param>
    Task OnTestRunEndAsync(int passed, int failed, int skipped);

    /// <summary>
    /// Called when an individual test starts.
    /// </summary>
    /// <param name="testName">The name of the test.</param>
    Task OnTestStartAsync(string testName);

    /// <summary>
    /// Called when an individual test ends.
    /// </summary>
    /// <param name="testName">The name of the test.</param>
    /// <param name="passed">Whether the test passed.</param>
    /// <param name="errorMessage">The error message, if the test failed.</param>
    Task OnTestEndAsync(string testName, bool passed, string? errorMessage = null);
}
