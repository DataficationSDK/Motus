using Microsoft.Extensions.Logging.Abstractions;
using Motus.Runner;
using Motus.Runner.Services;
using Motus.Runner.Services.Models;
using Motus.Runner.Services.Timeline;

namespace Motus.Tests.Runner;

[TestClass]
public class TrxRerunServiceTests
{
    private static string AssemblyPath => typeof(TrxRerunServiceTests).Assembly.Location;
    private static string FixturePassFullName =>
        $"{typeof(TrxRerunTarget).FullName}.{nameof(TrxRerunTarget.PassingTest)}";

    private static (TrxRerunService rerun, TestSessionService session, RunnerOptions options) BuildHarness()
    {
        var options = new RunnerOptions
        {
            ViewerMode = ViewerMode.Trx,
            TrxFilePath = "/tmp/fake.trx",
        };
        var discovery = new TestDiscovery(NullLogger<TestDiscovery>.Instance);
        var executor = new TestExecutionService(NullLogger<TestExecutionService>.Instance);
        var timeline = new TimelineService();
        var session = new TestSessionService(discovery, executor, timeline);
        var rerun = new TrxRerunService(options, session, discovery, NullLogger<TrxRerunService>.Instance);
        return (rerun, session, options);
    }

    private static DiscoveredTest MakeTrxTest(string fullName, string codeBase) =>
        new(TestClass: null, TestMethod: null, FullName: fullName, AssemblyName: "Test", IsIgnored: false, CodeBase: codeBase);

    [TestMethod]
    public async Task RerunTestAsync_AssemblyMissing_WritesErrorAndStaysInTrxMode()
    {
        var (rerun, session, options) = BuildHarness();
        var trxTest = MakeTrxTest("Some.Class.Test", "/no/such/file.dll");
        session.LoadFromTrxResults([trxTest], new Dictionary<string, TestNodeState>
        {
            ["Some.Class.Test"] = new("Some.Class.Test", TestStatus.Failed, null, "old", null),
        });

        var eventFired = false;
        rerun.ViewerModeChanged += () => eventFired = true;

        await rerun.RerunTestAsync("Some.Class.Test");

        Assert.AreEqual(ViewerMode.Trx, options.ViewerMode);
        Assert.IsFalse(rerun.IsRerunSession);
        Assert.IsFalse(eventFired);
        StringAssert.Contains(session.States["Some.Class.Test"].ErrorMessage ?? "", "Assembly not found");
    }

    [TestMethod]
    public async Task RerunTestAsync_MethodMissing_WritesErrorAndStaysInTrxMode()
    {
        var (rerun, session, options) = BuildHarness();
        var trxTest = MakeTrxTest("Bogus.Namespace.Class.DoesNotExist", AssemblyPath);
        session.LoadFromTrxResults([trxTest], new Dictionary<string, TestNodeState>
        {
            [trxTest.FullName] = new(trxTest.FullName, TestStatus.Failed, null, "old", null),
        });

        var eventFired = false;
        rerun.ViewerModeChanged += () => eventFired = true;

        await rerun.RerunTestAsync(trxTest.FullName);

        Assert.AreEqual(ViewerMode.Trx, options.ViewerMode);
        Assert.IsFalse(rerun.IsRerunSession);
        Assert.IsFalse(eventFired);
        StringAssert.Contains(session.States[trxTest.FullName].ErrorMessage ?? "", "not found in assembly");
    }

    [TestMethod]
    public async Task RerunTestAsync_Success_FlipsModeToRunnerAndPasses()
    {
        var (rerun, session, options) = BuildHarness();
        var trxTest = MakeTrxTest(FixturePassFullName, AssemblyPath);
        session.LoadFromTrxResults([trxTest], new Dictionary<string, TestNodeState>
        {
            [trxTest.FullName] = new(trxTest.FullName, TestStatus.Failed, null, "old", null),
        });

        var eventFired = false;
        rerun.ViewerModeChanged += () => eventFired = true;

        await rerun.RerunTestAsync(trxTest.FullName);

        Assert.AreEqual(ViewerMode.Runner, options.ViewerMode);
        Assert.IsTrue(rerun.IsRerunSession);
        Assert.IsTrue(eventFired);
        Assert.AreEqual(trxTest.FullName, rerun.LastRerunTestFullName);
        Assert.AreEqual(TestStatus.Passed, session.States[trxTest.FullName].Status);
    }

    [TestMethod]
    public async Task BackToResults_FromRunner_FlipsModeBackToTrx()
    {
        var (rerun, session, options) = BuildHarness();
        var trxTest = MakeTrxTest(FixturePassFullName, AssemblyPath);
        session.LoadFromTrxResults([trxTest], new Dictionary<string, TestNodeState>
        {
            [trxTest.FullName] = new(trxTest.FullName, TestStatus.Failed, null, null, null),
        });
        await rerun.RerunTestAsync(trxTest.FullName);
        Assert.AreEqual(ViewerMode.Runner, options.ViewerMode);

        var eventCount = 0;
        rerun.ViewerModeChanged += () => eventCount++;

        rerun.BackToResults();

        Assert.AreEqual(ViewerMode.Trx, options.ViewerMode);
        Assert.AreEqual(1, eventCount);
        Assert.IsTrue(rerun.IsRerunSession);
    }

    [TestMethod]
    public async Task RerunTestAsync_OtherTestStatesUnchanged()
    {
        var (rerun, session, _) = BuildHarness();
        var target = MakeTrxTest(FixturePassFullName, AssemblyPath);
        var other1 = MakeTrxTest("Some.Other.Test1", AssemblyPath);
        var other2 = MakeTrxTest("Some.Other.Test2", AssemblyPath);
        var originalOther1 = new TestNodeState("Some.Other.Test1", TestStatus.Failed, TimeSpan.FromMilliseconds(40), "old1", "stack1");
        var originalOther2 = new TestNodeState("Some.Other.Test2", TestStatus.Skipped, null, "old2", null);
        session.LoadFromTrxResults([target, other1, other2], new Dictionary<string, TestNodeState>
        {
            [target.FullName] = new(target.FullName, TestStatus.Failed, null, null, null),
            [other1.FullName] = originalOther1,
            [other2.FullName] = originalOther2,
        });

        await rerun.RerunTestAsync(target.FullName);

        Assert.AreEqual(originalOther1, session.States[other1.FullName]);
        Assert.AreEqual(originalOther2, session.States[other2.FullName]);
        Assert.AreEqual(TestStatus.Passed, session.States[target.FullName].Status);
    }

    [TestMethod]
    public async Task RerunTestAsync_UnknownTest_NoOp()
    {
        var (rerun, session, options) = BuildHarness();
        session.LoadFromTrxResults([], new Dictionary<string, TestNodeState>());

        await rerun.RerunTestAsync("does.not.exist");

        Assert.AreEqual(ViewerMode.Trx, options.ViewerMode);
        Assert.IsFalse(rerun.IsRerunSession);
    }

    // Nested fixture used as the rerun target. Public so reflection from
    // TestDiscovery picks it up; PassingTest also runs during normal
    // `dotnet test` execution where it is a no-op.
    [TestClass]
    public class TrxRerunTarget
    {
        [TestMethod]
        public void PassingTest()
        {
        }
    }
}
