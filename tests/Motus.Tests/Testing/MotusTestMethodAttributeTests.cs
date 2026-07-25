using System.Reflection;
using Motus.Testing.MSTest;
using BrowserFailure = Motus.Abstractions.BrowserFailure;
using MotusTargetClosedException = Motus.Abstractions.MotusTargetClosedException;

namespace Motus.Tests.Testing;

[TestClass]
public class MotusTestMethodAttributeTests
{
    [TestMethod]
    public void ARunThatLostTheBrowser_IsTried_Again()
    {
        var testMethod = new ScriptedTestMethod(LostBrowser(), Passed());

        var results = new MotusTestMethodAttribute().Execute(testMethod);

        Assert.AreEqual(2, testMethod.Invocations);
        Assert.AreEqual(UnitTestOutcome.Passed, results[0].Outcome);
    }

    [TestMethod]
    public void AnAssertionThatFailed_IsLeftAlone()
    {
        // The browser answered and the test disagreed with what it said. Running that again until
        // it agrees is how a suite stops meaning anything.
        var testMethod = new ScriptedTestMethod(
            Failed(new AssertFailedException("expected 'motus', found ''")), Passed());

        var results = new MotusTestMethodAttribute().Execute(testMethod);

        Assert.AreEqual(1, testMethod.Invocations);
        Assert.AreEqual(UnitTestOutcome.Failed, results[0].Outcome);
    }

    [TestMethod]
    public void ABrowserThatKeepsDying_IsGivenUpOn()
    {
        var testMethod = new ScriptedTestMethod(LostBrowser(), LostBrowser(), LostBrowser(), Passed());

        var results = new MotusTestMethodAttribute { Retries = 2 }.Execute(testMethod);

        Assert.AreEqual(3, testMethod.Invocations);
        Assert.AreEqual(UnitTestOutcome.Failed, results[0].Outcome);
    }

    [TestMethod]
    public void RetriesOfZero_RunsTheTestOnce()
    {
        var testMethod = new ScriptedTestMethod(LostBrowser(), Passed());

        new MotusTestMethodAttribute { Retries = 0 }.Execute(testMethod);

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
    public void TheClassAttribute_KeepsWhatAnotherKindOfTestMethodDoes()
    {
        var inner = new CountingTestMethodAttribute();
        var wrapped = new MotusTestClassAttribute().GetTestMethodAttribute(inner);
        var testMethod = new ScriptedTestMethod(Passed());

        wrapped!.Execute(testMethod);

        Assert.AreEqual(1, inner.Executions, "the wrapped attribute should be the one that runs the test");
    }

    [TestMethod]
    public void ALostBrowser_IsRecognisedThroughTheExceptionItWasWrappedIn()
    {
        var lost = new MotusTargetClosedException("page", "A1", "CDP WebSocket disconnected.");

        Assert.IsTrue(BrowserFailure.IsBrowserLost(lost));
        Assert.IsTrue(BrowserFailure.IsBrowserLost(new InvalidOperationException("while typing", lost)));
        Assert.IsFalse(BrowserFailure.IsBrowserLost(new InvalidOperationException("something else")));
        Assert.IsFalse(BrowserFailure.IsBrowserLost(null));
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

        public TestResult Invoke(object?[]? arguments)
        {
            var result = results[Math.Min(Invocations, results.Length - 1)];
            Invocations++;
            return result;
        }

        public string TestMethodName => nameof(ScriptedTestMethod);
        public string TestClassName => "Motus.Tests.Testing";
        public Type ReturnType => typeof(Task);
        public object?[]? Arguments => null;
        public ParameterInfo[] ParameterTypes => [];
        public MethodInfo MethodInfo => typeof(ScriptedTestMethod).GetMethod(nameof(Invoke))!;

        public Attribute[]? GetAllAttributes(bool inherit) => [];

        public TAttributeType[] GetAttributes<TAttributeType>(bool inherit)
            where TAttributeType : Attribute => [];
    }

    /// <summary>
    /// Stands in for a test method attribute that does something of its own, such as running a row
    /// of data per attempt.
    /// </summary>
    private sealed class CountingTestMethodAttribute : TestMethodAttribute
    {
        internal int Executions { get; private set; }

        public override TestResult[] Execute(ITestMethod testMethod)
        {
            Executions++;
            return base.Execute(testMethod);
        }
    }
}
