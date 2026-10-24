using System.Reflection;
using System.Runtime.CompilerServices;
using Motus.Testing.MSTest;
using MotusTargetClosedException = Motus.Abstractions.MotusTargetClosedException;

namespace Motus.Testing.MSTest.v4.Tests;

/// <summary>
/// The retry through MSTest 4's asynchronous <c>ExecuteAsync</c>, which replaced the
/// <c>Execute</c> the MSTest 3 build overrides.
/// </summary>
[TestClass]
public class MotusTestMethodAttributeTests
{
    [TestMethod]
    public async Task ARunThatLostTheBrowser_IsTried_Again()
    {
        var testMethod = new ScriptedTestMethod(LostBrowser(), Passed());

        var results = await new MotusTestMethodAttribute().ExecuteAsync(testMethod);

        Assert.AreEqual(2, testMethod.Invocations);
        Assert.AreEqual(UnitTestOutcome.Passed, results[0].Outcome);
    }

    [TestMethod]
    public async Task AnAssertionThatFailed_IsLeftAlone()
    {
        var testMethod = new ScriptedTestMethod(
            Failed(new AssertFailedException("expected 'motus', found ''")), Passed());

        var results = await new MotusTestMethodAttribute().ExecuteAsync(testMethod);

        Assert.AreEqual(1, testMethod.Invocations);
        Assert.AreEqual(UnitTestOutcome.Failed, results[0].Outcome);
    }

    [TestMethod]
    public async Task ABrowserThatKeepsDying_IsGivenUpOn()
    {
        var testMethod = new ScriptedTestMethod(LostBrowser(), LostBrowser(), LostBrowser(), Passed());

        var results = await new MotusTestMethodAttribute { Retries = 2 }.ExecuteAsync(testMethod);

        Assert.AreEqual(3, testMethod.Invocations);
        Assert.AreEqual(UnitTestOutcome.Failed, results[0].Outcome);
    }

    [TestMethod]
    public async Task RetriesOfZero_RunsTheTestOnce()
    {
        var testMethod = new ScriptedTestMethod(LostBrowser(), Passed());

        await new MotusTestMethodAttribute { Retries = 0 }.ExecuteAsync(testMethod);

        Assert.AreEqual(1, testMethod.Invocations);
    }

    [TestMethod]
    public void TheClassAttribute_GivesEveryTestTheRetry()
    {
        var wrapped = new MotusTestClassAttribute { Retries = 3 }
            .GetTestMethodAttribute(new TestMethodAttribute());

        var motus = wrapped as MotusTestMethodAttribute;
        Assert.IsNotNull(motus, "a plain test method should come back wrapped");
        Assert.AreEqual(3, motus.Retries);
    }

    [TestMethod]
    public void TheClassAttribute_LeavesAMethodThatAskedForItself()
    {
        var asked = new MotusTestMethodAttribute { Retries = 5 };

        var wrapped = new MotusTestClassAttribute { Retries = 1 }.GetTestMethodAttribute(asked);

        Assert.AreSame(asked, wrapped);
    }

    [TestMethod]
    public async Task TheClassAttribute_KeepsWhatAnotherKindOfTestMethodDoes()
    {
        var inner = new CountingTestMethodAttribute();
        var wrapped = new MotusTestClassAttribute().GetTestMethodAttribute(inner);
        var testMethod = new ScriptedTestMethod(Passed());

        await wrapped!.ExecuteAsync(testMethod);

        Assert.AreEqual(1, inner.Executions, "the wrapped attribute should be the one that runs the test");
    }

    [TestMethod]
    public void AWrappedTestMethod_KeepsItsSourceLocation()
    {
        // MSTest 4 takes the test explorer to a test's source by the location its attribute
        // carries. A wrapper that dropped it would leave every test in the class without one.
        var inner = MethodBase.GetCurrentMethod()!.GetCustomAttribute<TestMethodAttribute>()!;

        var wrapped = new MotusTestClassAttribute().GetTestMethodAttribute(inner)!;

        StringAssert.EndsWith(inner.DeclaringFilePath, "MotusTestMethodAttributeTests.cs");
        Assert.AreEqual(inner.DeclaringFilePath, wrapped.DeclaringFilePath);
        Assert.AreEqual(inner.DeclaringLineNumber, wrapped.DeclaringLineNumber);
    }

    [TestMethod]
    public void AppliedToAMethod_ItRecordsWhereItWasWritten()
    {
        var attribute = typeof(LostBrowserRetryTests)
            .GetMethod(nameof(LostBrowserRetryTests.ATestWhoseBrowserWentAway_RunsAgainWithASetupOfItsOwn))!
            .GetCustomAttribute<MotusTestMethodAttribute>()!;

        StringAssert.EndsWith(attribute.DeclaringFilePath, "LostBrowserRetryTests.cs");
        Assert.IsTrue(attribute.DeclaringLineNumber > 0);
    }

    private static TestResult Passed() => new() { Outcome = UnitTestOutcome.Passed };

    private static TestResult Failed(Exception failure)
        => new() { Outcome = UnitTestOutcome.Failed, TestFailureException = failure };

    private static TestResult LostBrowser()
        => Failed(new MotusTargetClosedException("page", "A1", "CDP WebSocket disconnected."));

    /// <summary>
    /// Hands back a fixed run of outcomes, one per attempt, and counts the attempts.
    /// </summary>
    private sealed class ScriptedTestMethod(params TestResult[] results) : ITestMethod
    {
        internal int Invocations { get; private set; }

        public Task<TestResult> InvokeAsync(object?[]? arguments)
        {
            var result = results[Math.Min(Invocations, results.Length - 1)];
            Invocations++;
            return Task.FromResult(result);
        }

        public string TestMethodName => nameof(ScriptedTestMethod);
        public string TestClassName => "Motus.Testing.MSTest.v4.Tests";
        public Type ReturnType => typeof(Task);
        public object?[]? Arguments => null;
        public ParameterInfo[] ParameterTypes => [];
        public MethodInfo MethodInfo => typeof(ScriptedTestMethod).GetMethod(nameof(InvokeAsync))!;

        public Attribute[] GetAllAttributes() => [];

        public TAttributeType[] GetAttributes<TAttributeType>()
            where TAttributeType : Attribute => [];
    }

    /// <summary>
    /// Stands in for a test method attribute that does something of its own, such as running a row
    /// of data per attempt.
    /// </summary>
    private sealed class CountingTestMethodAttribute(
        [CallerFilePath] string callerFilePath = "",
        [CallerLineNumber] int callerLineNumber = -1)
        : TestMethodAttribute(callerFilePath, callerLineNumber)
    {
        internal int Executions { get; private set; }

        public override Task<TestResult[]> ExecuteAsync(ITestMethod testMethod)
        {
            Executions++;
            return base.ExecuteAsync(testMethod);
        }
    }
}
