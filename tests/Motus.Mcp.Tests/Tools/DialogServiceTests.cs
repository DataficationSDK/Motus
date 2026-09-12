using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class DialogServiceTests
{
    private static FakeToolPage Page() => new(new AccessibilitySnapshot([], 0, null));

    [TestMethod]
    public void TakePendingDialog_WhenNone_ReturnsNull()
    {
        var service = new DialogService();
        Assert.IsNull(service.TakePendingDialog());
    }

    [TestMethod]
    public void Subscribe_ThenDialogFires_CapturesIt()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);

        var dialog = new FakeDialog(DialogType.Alert, "hi");
        page.RaiseDialog(dialog);

        Assert.AreSame(dialog, service.TakePendingDialog());
    }

    [TestMethod]
    public void TakePendingDialog_ClearsAfterReturning()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);
        page.RaiseDialog(new FakeDialog());

        Assert.IsNotNull(service.TakePendingDialog());
        Assert.IsNull(service.TakePendingDialog());
    }

    [TestMethod]
    public void SecondDialog_OverwritesTheFirst()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);

        page.RaiseDialog(new FakeDialog(DialogType.Alert, "first"));
        var second = new FakeDialog(DialogType.Confirm, "second");
        page.RaiseDialog(second);

        Assert.AreSame(second, service.TakePendingDialog());
    }

    [TestMethod]
    public void Subscribe_DifferentPage_StopsCapturingFromThePrevious()
    {
        var service = new DialogService();
        var first = Page();
        var second = Page();

        service.Subscribe(first);
        service.Subscribe(second);

        first.RaiseDialog(new FakeDialog(DialogType.Alert, "stale"));
        Assert.IsNull(service.TakePendingDialog(), "the previous page should have been unsubscribed");

        var live = new FakeDialog(DialogType.Alert, "live");
        second.RaiseDialog(live);
        Assert.AreSame(live, service.TakePendingDialog());
    }

    [TestMethod]
    public void PeekPendingDialog_DoesNotClearIt()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);
        var dialog = new FakeDialog(DialogType.Confirm, "Delete this?");
        page.RaiseDialog(dialog);

        Assert.AreSame(dialog, service.PeekPendingDialog());
        Assert.AreSame(dialog, service.PeekPendingDialog(), "a peek should be repeatable.");
        Assert.AreSame(dialog, service.TakePendingDialog(), "handle_dialog still has to find it.");
        Assert.IsNull(service.PeekPendingDialog());
    }

    [TestMethod]
    public async Task ArmAsync_CompletesWithTheNextDialog()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);

        var armed = service.ArmAsync();
        Assert.IsFalse(armed.IsCompleted, "nothing has opened a dialog yet.");

        var dialog = new FakeDialog(DialogType.Alert, "Are you sure?");
        page.RaiseDialog(dialog);

        Assert.AreSame(dialog, await armed);
    }

    [TestMethod]
    public async Task ArmAsync_WithADialogAlreadyPending_CompletesAtOnce()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);
        page.RaiseDialog(new FakeDialog(DialogType.Alert, "still here"));

        var armed = service.ArmAsync();

        Assert.AreEqual("still here", (await armed).Message);
    }

    [TestMethod]
    public void Disarm_LeavesALaterDialogPendingWithoutCompletingTheArmedTask()
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);

        var armed = service.ArmAsync();
        service.Disarm();
        page.RaiseDialog(new FakeDialog(DialogType.Alert, "after the action"));

        Assert.IsFalse(armed.IsCompleted, "the action that armed this one has already returned.");
        Assert.IsNotNull(service.PeekPendingDialog(), "the dialog is still open and still has to be reported.");
    }

    [TestMethod]
    public void Policy_Accept_AnswersTheDialogAndLeavesNothingPending()
    {
        var service = new DialogService { Policy = DialogPolicy.Accept };
        var page = Page();
        service.Subscribe(page);

        var dialog = new FakeDialog(DialogType.Confirm, "Delete this?");
        page.RaiseDialog(dialog);

        Assert.IsTrue(dialog.Accepted);
        Assert.IsNull(service.PeekPendingDialog(), "an answered dialog is not waiting for anyone.");
    }

    [TestMethod]
    public void Policy_Dismiss_AnswersTheDialogAndLeavesNothingPending()
    {
        var service = new DialogService { Policy = DialogPolicy.Dismiss };
        var page = Page();
        service.Subscribe(page);

        var dialog = new FakeDialog(DialogType.Confirm, "Delete this?");
        page.RaiseDialog(dialog);

        Assert.IsTrue(dialog.Dismissed);
        Assert.IsNull(service.PeekPendingDialog());
    }

    [TestMethod]
    public void Policy_Ask_IsTheDefault()
    {
        Assert.AreEqual(DialogPolicy.Ask, new DialogService().Policy);
    }

    [TestMethod]
    public void Subscribe_SamePageTwice_DoesNotDoubleCapture()
    {
        var service = new DialogService();
        var page = Page();

        service.Subscribe(page);
        service.Subscribe(page);

        // A single fire should leave a single pending dialog (no duplicate handlers).
        page.RaiseDialog(new FakeDialog());
        Assert.IsNotNull(service.TakePendingDialog());
        Assert.IsNull(service.TakePendingDialog());
    }

    [TestMethod]
    public void Policy_ComesFromTheLaunchOptions()
    {
        Assert.AreEqual(DialogPolicy.Accept, new DialogService(new McpServerLaunchOptions { Dialogs = "accept" }).Policy);
        Assert.AreEqual(DialogPolicy.Dismiss, new DialogService(new McpServerLaunchOptions { Dialogs = "Dismiss" }).Policy);
        Assert.AreEqual(DialogPolicy.Ask, new DialogService(new McpServerLaunchOptions { Dialogs = "ask" }).Policy);
        Assert.AreEqual(DialogPolicy.Ask, new DialogService(new McpServerLaunchOptions()).Policy);
        Assert.AreEqual(DialogPolicy.Ask, new DialogService().Policy);
    }
}
