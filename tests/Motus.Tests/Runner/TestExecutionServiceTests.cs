using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Motus.Runner.Services;
using Motus.Runner.Services.Models;

namespace Motus.Tests.Runner;

/// <summary>
/// The visual runner gives MSTest tests what MSTest's own adapter does: a <c>TestContext</c>
/// whose outcome is the test's own during cleanup, data rows, and cleanup derived class first.
/// </summary>
[TestClass]
public class TestExecutionServiceTests
{
    #region Fixtures (not [TestClass]: run only through TestExecutionService)

    public abstract class BrowserTestBase
    {
        public static List<string> Log { get; } = [];

        public TestContext TestContext { get; set; } = null!;

        [TestInitialize]
        public void BaseInit() => Log.Add("base init");

        // The check MotusTestBase makes before it decides whether to keep a failure trace.
        [TestCleanup]
        public void BaseCleanup() => Log.Add($"base cleanup saveTrace={TestContext?.CurrentTestOutcome != UnitTestOutcome.Passed}");
    }

    public class SignInFixture : BrowserTestBase
    {
        public static TestContext? ClassContext { get; set; }

        [ClassInitialize]
        public static void ClassSetup(TestContext context) => ClassContext = context;

        [TestCleanup]
        public void DerivedCleanup() => Log.Add("derived cleanup");

        public void UsesContext() => TestContext.WriteLine("hi");

        public void Fails() => throw new InvalidOperationException("intentional failure");

        public void SignsIn(string role) => Log.Add($"signs in as {role}");
    }

    #endregion

    private static TestExecutionService CreateService() => new(NullLogger<TestExecutionService>.Instance);

    private static DiscoveredTest MakeTest(string methodName, string? fullName = null, object?[]? arguments = null)
    {
        var type = typeof(SignInFixture);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return new DiscoveredTest(type, method, fullName ?? $"{type.FullName}.{methodName}", "Motus.Tests", false,
            Arguments: arguments);
    }

    private static async Task<List<TestNodeState>> RunAsync(params DiscoveredTest[] tests)
    {
        var states = new List<TestNodeState>();
        await CreateService().ExecuteAsync([.. tests], s => { lock (states) states.Add(s); }, CancellationToken.None);
        return states.Where(s => s.Status != TestStatus.Running).ToList();
    }

    [TestMethod]
    public async Task PassingTest_HasATestContext_AndCleanupSeesItPassed()
    {
        BrowserTestBase.Log.Clear();

        var states = await RunAsync(MakeTest(nameof(SignInFixture.UsesContext)));

        Assert.AreEqual(TestStatus.Passed, states.Single().Status, states.Single().ErrorMessage);
        CollectionAssert.AreEqual(
            new[] { "base init", "derived cleanup", "base cleanup saveTrace=False" },
            BrowserTestBase.Log);
    }

    [TestMethod]
    public async Task FailingTest_CleanupSeesItFailed()
    {
        BrowserTestBase.Log.Clear();

        var states = await RunAsync(MakeTest(nameof(SignInFixture.Fails)));

        Assert.AreEqual(TestStatus.Failed, states.Single().Status);
        CollectionAssert.Contains(BrowserTestBase.Log, "base cleanup saveTrace=True");
    }

    [TestMethod]
    public async Task DataRowTest_RunsWithItsArguments()
    {
        BrowserTestBase.Log.Clear();
        var fullName = $"{typeof(SignInFixture).FullName}.SignsIn (owner)";

        var states = await RunAsync(MakeTest(nameof(SignInFixture.SignsIn), fullName, ["owner"]));

        Assert.AreEqual(TestStatus.Passed, states.Single().Status, states.Single().ErrorMessage);
        Assert.AreEqual(fullName, states.Single().FullName);
        CollectionAssert.Contains(BrowserTestBase.Log, "signs in as owner");
    }

    [TestMethod]
    public async Task ClassInitialize_RunsWithATestContext()
    {
        SignInFixture.ClassContext = null;

        await RunAsync(MakeTest(nameof(SignInFixture.UsesContext)));

        Assert.IsNotNull(SignInFixture.ClassContext, "the visual runner should run [ClassInitialize]");
        Assert.AreEqual(typeof(SignInFixture).FullName, SignInFixture.ClassContext.FullyQualifiedTestClassName);
    }

    [TestMethod]
    public async Task Discovery_ExpandsDataRows()
    {
        var discovery = new TestDiscovery(NullLogger<TestDiscovery>.Instance);

        var tests = await discovery.DiscoverAsync([typeof(RunnerDataRowFixture).Assembly.Location], nameof(RunnerDataRowFixture));

        CollectionAssert.AreEquivalent(
            new[]
            {
                $"{typeof(RunnerDataRowFixture).FullName}.Doubles (1,2)",
                $"{typeof(RunnerDataRowFixture).FullName}.Doubles (2,4)",
            },
            tests.Select(t => t.FullName).ToArray());
        Assert.IsTrue(tests.All(t => t.ClassFullName == typeof(RunnerDataRowFixture).FullName && t.MethodName == nameof(RunnerDataRowFixture.Doubles)));
        Assert.IsTrue(tests.All(t => t.CodeBase == typeof(RunnerDataRowFixture).Assembly.Location));
    }

    [TestMethod]
    public async Task DiscoveredDataRows_RunInATestHost_WithTheirArguments()
    {
        var discovery = new TestDiscovery(NullLogger<TestDiscovery>.Instance);
        var tests = await discovery.DiscoverAsync([typeof(RunnerDataRowFixture).Assembly.Location], nameof(RunnerDataRowFixture));

        var states = new List<TestNodeState>();
        await CreateService().ExecuteAsync(tests, s => { lock (states) states.Add(s); }, CancellationToken.None);

        var finished = states.Where(s => s.Status != TestStatus.Running).ToList();
        Assert.AreEqual(2, finished.Count);
        Assert.IsTrue(finished.All(s => s.Status == TestStatus.Passed), string.Join(" | ", finished.Select(s => s.ErrorMessage)));
    }
}

/// <summary>A data-driven test MSTest runs as well, discovered by the visual runner in the test above.</summary>
[TestClass]
public class RunnerDataRowFixture
{
    [DataTestMethod]
    [DataRow(1, 2)]
    [DataRow(2, 4)]
    public void Doubles(int value, int doubled) => Assert.AreEqual(doubled, value * 2);
}
