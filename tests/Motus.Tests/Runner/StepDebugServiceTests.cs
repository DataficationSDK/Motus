using Motus.Runner.Services.Timeline;

namespace Motus.Tests.Runner;

[TestClass]
public class StepDebugServiceTests
{
    [TestMethod]
    public async Task Disabled_WaitReturnsImmediately()
    {
        var svc = new StepDebugService();

        // Should not block when step mode is off
        await svc.WaitIfPausedAsync("click", "#btn", CancellationToken.None);

        Assert.IsFalse(svc.IsPaused);
    }

    [TestMethod]
    public async Task Enabled_WaitBlocksUntilAdvance()
    {
        var svc = new StepDebugService();
        svc.EnableStepMode();

        var waitTask = Task.Run(() => svc.WaitIfPausedAsync("click", "#btn", CancellationToken.None));

        // Give it a moment to enter the paused state
        await Task.Delay(50);
        Assert.IsTrue(svc.IsPaused);
        Assert.AreEqual("click", svc.PendingActionType);
        Assert.AreEqual("#btn", svc.PendingSelector);

        svc.Advance();
        await waitTask;

        Assert.IsFalse(svc.IsPaused);
    }

    [TestMethod]
    public async Task Resume_UnblocksAndDisables()
    {
        var svc = new StepDebugService();
        svc.EnableStepMode();

        var waitTask = Task.Run(() => svc.WaitIfPausedAsync("fill", "input", CancellationToken.None));

        await Task.Delay(50);
        Assert.IsTrue(svc.IsPaused);

        svc.Resume();
        await waitTask;

        Assert.IsFalse(svc.IsStepMode);
        Assert.IsFalse(svc.IsPaused);
    }

    [TestMethod]
    public async Task CancellationToken_Unblocks()
    {
        var svc = new StepDebugService();
        svc.EnableStepMode();

        using var cts = new CancellationTokenSource(100);

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(
            () => svc.WaitIfPausedAsync("click", "#btn", cts.Token));
    }
}
