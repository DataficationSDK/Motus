using System.Reflection;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// The outcome a running test reports through MSTest's <c>TestContext.CurrentTestOutcome</c>.
/// </summary>
internal enum TestCaseOutcome
{
    InProgress,
    Passed,
    Failed,
    Inconclusive,
}

/// <summary>
/// What the runners know about the test being run, from which an MSTest <c>TestContext</c> is
/// built. One is made per test case, and one for each <c>[AssemblyInitialize]</c> and
/// <c>[ClassInitialize]</c> call.
/// </summary>
internal sealed class TestCaseContext
{
    public TestCaseContext(Type testClass, MethodInfo? testMethod, string? displayName = null, object?[]? arguments = null)
    {
        TestClass = testClass;
        TestMethod = testMethod;
        DisplayName = displayName ?? testMethod?.Name;
        Arguments = arguments;
    }

    public Type TestClass { get; }

    /// <summary>The test method, or null for an assembly or class initializer.</summary>
    public MethodInfo? TestMethod { get; }

    /// <summary>The name of this test case, including its data row's arguments.</summary>
    public string? DisplayName { get; }

    /// <summary>The data row this test case runs with, or null for a test without one.</summary>
    public object?[]? Arguments { get; }

    /// <summary>
    /// <see cref="TestCaseOutcome.InProgress"/> while the test is set up and run, then the result
    /// of the test itself, which is what the test's cleanup methods see.
    /// </summary>
    public TestCaseOutcome Outcome { get; set; } = TestCaseOutcome.InProgress;

    /// <summary>
    /// Cancelled when the run is stopped. Not disposed, because a test may keep its
    /// <c>TestContext</c> in a static field and read the token after it has finished.
    /// </summary>
    public CancellationTokenSource CancellationTokenSource { get; } = new();
}
