using Motus.Abstractions;
using Motus.Cli.Services;
using Motus.Cli.Services.Reporters;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class ShardMergerTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"motus-shard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string Path_(string name) => Path.Combine(_dir, name);

    private static (TestInfo, TestResult) Pass(string name) =>
        (new TestInfo(name, "Suite"), new TestResult(name, true, 10));

    private static (TestInfo, TestResult) Fail(string name) =>
        (new TestInfo(name, "Suite"), new TestResult(name, false, 10, "boom", "stack"));

    private static (TestInfo, TestResult) Flaky(string name) =>
        (new TestInfo(name, "Suite"), new TestResult(name, true, 10, Flaky: true, Attempts: 2));

    private static (TestInfo, TestResult) QuarantinedFail(string name) =>
        (new TestInfo(name, "Suite"), new TestResult(name, false, 10, "qfail", null, Quarantined: true));

    private static TestRunSummary BuildSummary(string suite, List<(TestInfo Info, TestResult Result)> cases)
    {
        var passed = cases.Count(c => c.Result.Passed && !c.Result.Quarantined);
        var failed = cases.Count(c => !c.Result.Passed && !c.Result.Quarantined);
        var quarantined = cases.Count(c => c.Result.Quarantined);
        var flaky = cases.Count(c => c.Result.Flaky && !c.Result.Quarantined);
        var duration = cases.Sum(c => c.Result.DurationMs);
        return new TestRunSummary(suite, passed, failed, 0, duration, flaky, quarantined);
    }

    private static async Task WriteShardAsync(
        IReporter reporter, string suite, int index, int total, List<(TestInfo Info, TestResult Result)> cases)
    {
        await reporter.OnTestRunStartAsync(new TestSuiteInfo(suite, cases.Count, ShardIndex: index, ShardTotal: total));
        foreach (var (info, result) in cases)
        {
            await reporter.OnTestStartAsync(info);
            await reporter.OnTestEndAsync(info, result);
        }
        await reporter.OnTestRunEndAsync(BuildSummary(suite, cases));
    }

    private async Task<string> WriteJUnitShardAsync(string file, int index, int total, params (TestInfo, TestResult)[] cases)
    {
        var path = Path_(file);
        await WriteShardAsync(new JUnitReporter(path), "Suite", index, total, cases.ToList());
        return path;
    }

    private async Task<string> WriteTrxShardAsync(string file, int index, int total, params (TestInfo, TestResult)[] cases)
    {
        var path = Path_(file);
        await WriteShardAsync(new TrxReporter(path), "Suite", index, total, cases.ToList());
        return path;
    }

    [TestMethod]
    public async Task Merge_SumsCountsAcrossPassingShards()
    {
        var f1 = await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"), Pass("Ns.B"));
        var f2 = await WriteJUnitShardAsync("r.shard-2.xml", 2, 2, Pass("Ns.C"), Pass("Ns.D"));

        var result = await ShardMerger.MergeAsync([f1, f2], [], expect: null);

        Assert.AreEqual(4, result.Passed);
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(2, result.FileCount);
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task Merge_FailingShard_MakesRunFail()
    {
        var f1 = await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"));
        var f2 = await WriteJUnitShardAsync("r.shard-2.xml", 2, 2, Fail("Ns.D"));

        var result = await ShardMerger.MergeAsync([f1, f2], [], expect: null);

        Assert.AreEqual(1, result.Failed);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task Merge_ExpectMatchingShards_Validates()
    {
        var f1 = await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"));
        var f2 = await WriteJUnitShardAsync("r.shard-2.xml", 2, 2, Pass("Ns.B"));

        var result = await ShardMerger.MergeAsync([f1, f2], [], expect: 2);

        Assert.IsTrue(result.ValidationPassed);
        Assert.AreEqual(0, result.Errors.Count);
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task Merge_MissingShard_FailsExpect()
    {
        var f1 = await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"));

        var result = await ShardMerger.MergeAsync([f1], [], expect: 2);

        Assert.IsFalse(result.ValidationPassed);
        Assert.IsTrue(result.Errors.Count > 0);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task Merge_DuplicateShard_FailsExpect()
    {
        var f1 = await WriteJUnitShardAsync("a.xml", 1, 2, Pass("Ns.A"));
        var f2 = await WriteJUnitShardAsync("b.xml", 1, 2, Pass("Ns.B"));

        var result = await ShardMerger.MergeAsync([f1, f2], [], expect: 2);

        Assert.IsFalse(result.ValidationPassed);
        Assert.IsFalse(result.Success);
    }

    [TestMethod]
    public async Task Merge_PreservesFlakyAndQuarantineBuckets()
    {
        var f1 = await WriteJUnitShardAsync("r.shard-1.xml", 1, 1,
            Pass("Ns.A"), Flaky("Ns.F"), QuarantinedFail("Ns.Q"));

        var result = await ShardMerger.MergeAsync([f1], [], expect: null);

        Assert.AreEqual(1, result.Flaky, "Flaky pass should be counted.");
        Assert.AreEqual(1, result.Quarantined, "Quarantined test should be in its own bucket.");
        Assert.AreEqual(2, result.Passed, "A clean pass plus the flaky pass.");
        Assert.AreEqual(0, result.Failed, "The quarantined failure must not count as a failure.");
        Assert.IsTrue(result.Success);
    }

    [TestMethod]
    public async Task Merge_MixedJUnitAndTrxInputs()
    {
        var junit = await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"), Fail("Ns.B"));
        var trx = await WriteTrxShardAsync("r.shard-2.trx", 2, 2, Pass("Ns.C"), Pass("Ns.D"));

        var result = await ShardMerger.MergeAsync([junit, trx], [], expect: 2);

        Assert.IsTrue(result.ValidationPassed, "Coords from both JUnit and TRX must validate.");
        Assert.AreEqual(3, result.Passed);
        Assert.AreEqual(1, result.Failed);
        Assert.IsFalse(result.Success, "A failure in any shard fails the merge.");
    }

    [TestMethod]
    public async Task Merge_WritesMergedOutputThatRoundTrips()
    {
        var f1 = await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"), Pass("Ns.B"));
        var f2 = await WriteJUnitShardAsync("r.shard-2.xml", 2, 2, Pass("Ns.C"), Pass("Ns.D"));
        var mergedJUnit = Path_("merged.xml");
        var mergedTrx = Path_("merged.trx");

        await ShardMerger.MergeAsync([f1, f2], [$"junit:{mergedJUnit}", $"trx:{mergedTrx}"], expect: 2);

        Assert.IsTrue(File.Exists(mergedJUnit));
        Assert.IsTrue(File.Exists(mergedTrx));

        // The merged file (no shard stamp) re-parses to the same summed counts.
        var reJUnit = await ShardMerger.MergeAsync([mergedJUnit], [], expect: null);
        Assert.AreEqual(4, reJUnit.Passed);

        var reTrx = await ShardMerger.MergeAsync([mergedTrx], [], expect: null);
        Assert.AreEqual(4, reTrx.Passed);
    }

    [TestMethod]
    public async Task ExpandInputs_ExpandsGlob()
    {
        await WriteJUnitShardAsync("r.shard-1.xml", 1, 2, Pass("Ns.A"));
        await WriteJUnitShardAsync("r.shard-2.xml", 2, 2, Pass("Ns.B"));

        var expanded = ShardMerger.ExpandInputs([Path_("r.shard-*.xml")]);

        Assert.AreEqual(2, expanded.Count);
    }
}
