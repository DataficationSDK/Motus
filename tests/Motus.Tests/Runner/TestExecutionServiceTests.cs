using System.Reflection;
using Microsoft.Extensions.Logging.Abstractions;
using Motus.Runner.Services;
using Motus.Runner.Services.Models;
using Motus.Runner.Services.Timeline;

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

    /// <summary>
    /// A run starts a fresh test host, which reads the data source again. A row that names itself
    /// differently on each read (a Guid in it) is still found, by its place in its data source.
    /// </summary>
    [TestMethod]
    public async Task DataRowsNamedDifferentlyEachRead_RunInAFreshTestHost()
    {
        var discovery = new TestDiscovery(NullLogger<TestDiscovery>.Instance);
        var tests = await discovery.DiscoverAsync([typeof(RunnerChangingRowsFixture).Assembly.Location], nameof(RunnerChangingRowsFixture));

        var states = new List<TestNodeState>();
        await CreateService().ExecuteAsync(tests, s => { lock (states) states.Add(s); }, CancellationToken.None);

        var finished = states.Where(s => s.Status != TestStatus.Running).ToList();
        Assert.AreEqual(3, finished.Count);
        Assert.IsTrue(finished.All(s => s.Status == TestStatus.Passed), string.Join(" | ", finished.Select(s => s.ErrorMessage)));
    }

    [TestMethod]
    public async Task DataRowTheSourceNoLongerReturns_SaysSo()
    {
        var method = typeof(RunnerChangingRowsFixture).GetMethod(nameof(RunnerChangingRowsFixture.RunsEachRow))!;
        var cases = Motus.Runner.Services.Execution.TestCaseDiscovery.Discover(typeof(RunnerChangingRowsFixture).Assembly, nameof(RunnerChangingRowsFixture));
        var host = new Motus.Runner.Services.Execution.InProcessTestHost(typeof(RunnerChangingRowsFixture).Assembly, cases);
        var gone = Motus.Runner.Services.Execution.TestCaseExpander.RowId(
            Motus.Runner.Services.Execution.TestCaseExpander.MethodId(typeof(RunnerChangingRowsFixture), method), 0, 7);

        var report = await host.RunAsync(gone, "RunnerChangingRowsFixture.RunsEachRow (7, gone)");

        Assert.IsFalse(report.Passed);
        StringAssert.Contains(report.ErrorMessage, "row 8 of its data source, which now returns 3 row(s)");
    }

    /// <summary>
    /// An assembly whose test host cannot start fails its own tests, and the other assemblies in the
    /// run still run, rather than the whole run stopping with the rest left as they were.
    /// </summary>
    [TestMethod]
    public async Task AssemblyThatCannotStart_FailsItsTests_AndTheOthersRun()
    {
        var path = Path.Combine(Path.GetTempPath(), $"motus-not-an-assembly-{Guid.NewGuid():N}.dll");
        await File.WriteAllTextAsync(path, "not an assembly");
        try
        {
            var broken = new DiscoveredTest(null, null, "Broken.Tests.Test", "Broken", false, CodeBase: path);
            var discovery = new TestDiscovery(NullLogger<TestDiscovery>.Instance);
            var good = await discovery.DiscoverAsync([typeof(RunnerDataRowFixture).Assembly.Location], nameof(RunnerDataRowFixture));

            var states = new List<TestNodeState>();
            await CreateService().ExecuteAsync([broken, .. good], s => { lock (states) states.Add(s); }, CancellationToken.None);

            var finished = states.Where(s => s.Status != TestStatus.Running).ToDictionary(s => s.FullName);
            Assert.AreEqual(TestStatus.Failed, finished[broken.FullName].Status);
            StringAssert.Contains(finished[broken.FullName].ErrorMessage, "Could not run the tests in Broken");
            Assert.IsTrue(good.All(t => finished[t.FullName].Status == TestStatus.Passed));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The visual runner's test host also installs its browser hooks, which reach further into Motus
    /// than a plain run. On the oldest published Motus the tests still run with their own results.
    /// </summary>
    [TestMethod]
    public async Task SuiteOnTheOldestPublishedMotus_RunsInTheVisualRunner()
    {
        var output = typeof(TestExecutionServiceTests).Assembly.GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
            .Single(a => a.Key == "OldMotusFixtureOutput").Value!;
        var path = Path.GetFullPath(Path.Combine(output, $"net{Environment.Version.Major}.0", "Motus.Fixtures.OldMotus.Tests.dll"));
        var timeline = new TimelineService();
        var service = new TestExecutionService(NullLogger<TestExecutionService>.Instance, null, new ScreencastService(), timeline, new StepDebugService());

        var tests = await new TestDiscovery(NullLogger<TestDiscovery>.Instance).DiscoverAsync([path], null);
        var states = new List<TestNodeState>();
        await service.ExecuteAsync(tests, s => { lock (states) states.Add(s); }, CancellationToken.None);

        var finished = states.Where(s => s.Status != TestStatus.Running).ToList();
        Assert.AreEqual(6, finished.Count);
        Assert.AreEqual(6, finished.Count(s => s.Status == TestStatus.Passed), string.Join(" | ", finished.Select(s => $"{s.FullName}: {s.ErrorMessage}")));
    }
}

/// <summary>Rows whose names change on every read of the data source, run by the visual runner above.</summary>
[TestClass]
public class RunnerChangingRowsFixture
{
    public static IEnumerable<object[]> Rows => Enumerable.Range(0, 3).Select(i => new object[] { i, Guid.NewGuid().ToString("N") });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void RunsEachRow(int index, string token) => Assert.IsTrue(index < 3 && token.Length == 32);
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
