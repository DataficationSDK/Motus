using Motus.Runner;
using Motus.Runner.Services.SelectorRepair;

namespace Motus.Tests.Runner;

[TestClass]
public class SelectorRepairServiceTests
{
    [TestCleanup]
    public void TearDown()
    {
        SelectorRepairBridge.Reset();
    }

    [TestMethod]
    public async Task Initialize_AdvancesToFirstItem()
    {
        var items = MakeItems(2);
        var calls = new List<(RepairQueueItem, string)>();
        SelectorRepairBridge.Begin(items, page: null,
            (item, replacement) => { calls.Add((item, replacement)); return new RepairOutcome(true, null); });

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });

        await svc.InitializeAsync();

        Assert.AreEqual(0, svc.CurrentIndex);
        Assert.AreSame(items[0], svc.CurrentItem);
        Assert.IsFalse(svc.IsComplete);
    }

    [TestMethod]
    public async Task Accept_FiredApply_AdvancesAndRecordsAccepted()
    {
        var items = MakeItems(2);
        var calls = new List<(RepairQueueItem, string)>();
        SelectorRepairBridge.Begin(items, page: null,
            (item, replacement) => { calls.Add((item, replacement)); return new RepairOutcome(true, null); });

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });
        await svc.InitializeAsync();

        await svc.AcceptAsync("GetByTestId(\"new\")");

        Assert.AreEqual(1, calls.Count);
        Assert.AreEqual("GetByTestId(\"new\")", calls[0].Item2);
        Assert.AreEqual(1, svc.Accepted);
        Assert.AreEqual(1, svc.CurrentIndex);
        Assert.AreSame(items[1], svc.CurrentItem);
    }

    [TestMethod]
    public async Task Accept_WhenApplyFails_RecordsFailedAndStaysOnItem()
    {
        var items = MakeItems(2);
        SelectorRepairBridge.Begin(items, page: null,
            (_, _) => new RepairOutcome(false, "rewrite failed"));

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });
        await svc.InitializeAsync();

        await svc.AcceptAsync("GetByTestId(\"new\")");

        Assert.AreEqual(1, svc.Failed);
        Assert.AreEqual(0, svc.CurrentIndex, "should not advance on failure");
        Assert.IsNotNull(svc.CurrentOutcome);
        Assert.IsFalse(svc.CurrentOutcome!.Fixed);
        Assert.AreEqual("rewrite failed", svc.CurrentOutcome.Error);
    }

    [TestMethod]
    public async Task Skip_AdvancesWithoutInvokingApply()
    {
        var items = MakeItems(2);
        var applyCalled = false;
        SelectorRepairBridge.Begin(items, page: null,
            (_, _) => { applyCalled = true; return new RepairOutcome(true, null); });

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });
        await svc.InitializeAsync();

        await svc.SkipAsync();

        Assert.IsFalse(applyCalled);
        Assert.AreEqual(1, svc.Skipped);
        Assert.AreEqual(1, svc.CurrentIndex);
    }

    [TestMethod]
    public async Task ExhaustQueue_CompletesBridgeWithSummary()
    {
        var items = MakeItems(2);
        SelectorRepairBridge.Begin(items, page: null,
            (_, _) => new RepairOutcome(true, null));

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });
        await svc.InitializeAsync();

        await svc.AcceptAsync("GetByTestId(\"a\")");
        await svc.AcceptAsync("GetByTestId(\"b\")");

        Assert.IsTrue(svc.IsComplete);
        Assert.AreEqual(2, svc.Accepted);

        var summary = await SelectorRepairBridge.Completion!.Task;
        Assert.AreEqual(2, summary.Accepted);
        Assert.AreEqual(0, summary.Skipped);
        Assert.AreEqual(0, summary.Failed);
    }

    [TestMethod]
    public async Task Finish_CompletesBridgeImmediately()
    {
        var items = MakeItems(3);
        SelectorRepairBridge.Begin(items, page: null,
            (_, _) => new RepairOutcome(true, null));

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });
        await svc.InitializeAsync();
        await svc.AcceptAsync("GetByTestId(\"a\")");

        await svc.FinishAsync();

        var summary = await SelectorRepairBridge.Completion!.Task;
        Assert.AreEqual(1, summary.Accepted);
        Assert.AreEqual(0, summary.Skipped);
    }

    [TestMethod]
    public async Task StateChanged_FiresOnAccept()
    {
        var items = MakeItems(2);
        SelectorRepairBridge.Begin(items, page: null,
            (_, _) => new RepairOutcome(true, null));

        var svc = new SelectorRepairService(new RunnerOptions { RepairMode = true });
        await svc.InitializeAsync();

        var fired = 0;
        svc.StateChanged += () => fired++;

        await svc.AcceptAsync("GetByTestId(\"a\")");

        Assert.IsTrue(fired >= 1);
    }

    private static List<RepairQueueItem> MakeItems(int count)
    {
        var list = new List<RepairQueueItem>();
        for (var i = 0; i < count; i++)
        {
            list.Add(new RepairQueueItem(
                Selector: $"#old-{i}",
                LocatorMethod: "Locator",
                SourceFile: $"/tmp/test{i}.cs",
                SourceLine: 10 + i,
                PageUrl: "data:text/html,<html></html>",
                Suggestions: new List<RepairCandidate>
                {
                    new($"GetByTestId(\"new-{i}\")", "data-testid", RepairConfidence.High),
                },
                HighlightSelector: null));
        }
        return list;
    }
}
