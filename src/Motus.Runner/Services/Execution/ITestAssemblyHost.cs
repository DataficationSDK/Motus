using System.Text.Json;
using System.Text.Json.Serialization;
using Motus.Abstractions;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// Runs the tests of one test assembly: its <c>[AssemblyInitialize]</c>, each test case on request,
/// then its class and assembly cleanup. <see cref="InProcessTestHost"/> runs them in this process;
/// <see cref="TestHostClient"/> runs them in a test host process of the assembly's own.
/// </summary>
internal interface ITestAssemblyHost : IAsyncDisposable
{
    /// <summary>
    /// Runs the assembly's <c>[AssemblyInitialize]</c> methods, once for the life of the host.
    /// Returns what went wrong, or null when they ran or there are none.
    /// </summary>
    Task<TestHostFailure?> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the discovered test case <paramref name="testId"/> once, or when no case has that id, the
    /// one named <paramref name="fullName"/>. Cancelling <paramref name="cancellationToken"/> cancels
    /// the token the test reads from its <c>TestContext</c>; the test still runs to its end.
    /// </summary>
    Task<TestCaseReport> RunAsync(string testId, string fullName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <c>[ClassCleanup]</c> and NUnit <c>[OneTimeTearDown]</c> for each class that ran a test,
    /// then the assembly's <c>[AssemblyCleanup]</c> methods.
    /// </summary>
    Task CleanupAsync(CancellationToken cancellationToken = default);
}

/// <summary>What discovery reports about one test case.</summary>
/// <param name="ClassName">The test class's full name.</param>
/// <param name="MethodName">The test method's name, without the data row.</param>
/// <param name="Id">The case's identity apart from its name, which runs ask for it by.</param>
internal sealed record TestCaseInfo(
    string FullName,
    string ClassName,
    string MethodName,
    bool IsIgnored,
    string? IgnoreReason,
    bool Quarantined,
    string Id);

/// <summary>An exception from test code, as text, so that it can cross a process boundary.</summary>
internal sealed record TestHostFailure(string Message, string? StackTrace);

/// <summary>
/// The outcome of one run of one test case, with what the run collected for the reporters.
/// </summary>
/// <remarks>
/// What the run collected is carried as JSON rather than as Motus types: the test host runs on the
/// suite's Motus, whose types can differ from the runner's. <see cref="TestCaseReportExtras"/> reads
/// it back into the runner's own.
/// </remarks>
/// <param name="TransientFailure">True when the failure was a lost browser connection, which the
/// default retry policy re-runs.</param>
internal sealed record TestCaseReport(
    string FullName,
    bool Passed,
    double DurationMs,
    string? ErrorMessage,
    string? StackTrace,
    bool TransientFailure = false,
    JsonElement? Violations = null,
    JsonElement? PerformanceMetrics = null,
    JsonElement? Coverage = null)
{
    [JsonIgnore]
    public TimeSpan Duration => TimeSpan.FromMilliseconds(DurationMs);

    /// <summary>A failed run that never reached the test.</summary>
    public static TestCaseReport NotRun(string fullName, string message)
        => new(fullName, false, 0, message, null);
}

/// <summary>
/// Reads what a test run collected out of its <see cref="TestCaseReport"/>, in the runner. Anything
/// that cannot be read is left out rather than failing the run.
/// </summary>
internal static class TestCaseReportExtras
{
    public static IReadOnlyList<AccessibilityViolation> Violations(TestCaseReport? report)
        => Read<List<AccessibilityViolation>>(report?.Violations) ?? [];

    public static PerformanceMetrics? PerformanceMetrics(TestCaseReport? report)
        => Read<PerformanceMetrics>(report?.PerformanceMetrics);

    public static CoverageData? Coverage(TestCaseReport? report)
        => Read<CoverageData>(report?.Coverage);

    private static T? Read<T>(JsonElement? json) where T : class
    {
        if (json is not { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } element)
            return null;

        try
        {
            return element.Deserialize<T>(TestHostJson.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
