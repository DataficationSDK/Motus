using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// The race every action runs: the action against the dialog it might open. A dialog stops the
/// browser answering input, so the command that opened it is the one command that cannot report
/// it, and without the race the call sits there until the transport gives up a minute later.
/// </summary>
[TestClass]
public class ActionRunnerTests
{
    private static FakeToolPage Page() => new(new AccessibilitySnapshot([], 0, null));

    private static string TextOf(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    [TestMethod]
    public async Task RunAsync_WhenADialogOpensDuringTheAction_ReportsItAndCancelsTheAction()
    {
        var dialogs = new DialogService();
        var page = Page();
        dialogs.Subscribe(page);

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken actionToken = default;

        var running = ActionRunner.RunAsync(dialogs, CancellationToken.None, async token =>
        {
            actionToken = token;
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token).ConfigureAwait(false);
            return ToolResultHelper.Text("the action finished");
        });

        await started.Task;
        page.RaiseDialog(new FakeDialog(DialogType.Alert, "Are you sure?"));

        var result = await running;

        Assert.AreEqual(
            "The action opened an alert dialog: \"Are you sure?\". Call handle_dialog to accept or dismiss it.",
            TextOf(result));
        Assert.IsFalse(result.IsError ?? false, "the action ran; the page is simply waiting on an answer.");
        Assert.IsTrue(actionToken.IsCancellationRequested, "the action's token should have been cancelled.");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheActionFinishesFirst_ReturnsItsOwnResult()
    {
        var dialogs = new DialogService();
        var page = Page();
        dialogs.Subscribe(page);

        var result = await ActionRunner.RunAsync(
            dialogs, CancellationToken.None, _ => Task.FromResult(ToolResultHelper.Text("Clicked e5")));

        Assert.AreEqual("Clicked e5", TextOf(result));

        // The arming is dropped with the action that raised it, so a dialog opened by something
        // else afterwards belongs to whatever call comes next, not to this finished one.
        page.RaiseDialog(new FakeDialog(DialogType.Alert, "later"));
        Assert.AreEqual("Clicked e5", TextOf(result));
    }

    [TestMethod]
    public async Task RunAsync_WithADialogAlreadyOpen_RefusesTheActionInsteadOfDispatchingIntoABlockedPage()
    {
        var dialogs = new DialogService();
        var page = Page();
        dialogs.Subscribe(page);
        page.RaiseDialog(new FakeDialog(DialogType.Confirm, "Delete this?"));

        var ran = false;
        var result = await ActionRunner.RunAsync(dialogs, CancellationToken.None, _ =>
        {
            ran = true;
            return Task.FromResult(ToolResultHelper.Text("Clicked e5"));
        });

        Assert.IsFalse(ran, "the action should not reach a page that is waiting on a dialog.");
        Assert.IsTrue(result.IsError ?? false);
        Assert.AreEqual(
            "A \"confirm\" dialog is open: \"Delete this?\". Handle it with handle_dialog before other actions.",
            TextOf(result));
        Assert.IsNotNull(dialogs.PeekPendingDialog(), "handle_dialog still has to find the dialog.");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheActionIsCancelled_SaysWhatItWasWaitingFor()
    {
        var dialogs = new DialogService();
        dialogs.Subscribe(Page());

        var result = await ActionRunner.RunAsync(
            dialogs, CancellationToken.None, _ => Task.FromException<CallToolResult>(new TaskCanceledException()));

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "Timed out after");
        StringAssert.Contains(TextOf(result), "waiting for the browser to answer");
        StringAssert.Contains(TextOf(result), "handle_dialog");
        Assert.IsFalse(TextOf(result).Contains("A task was canceled", StringComparison.Ordinal),
            "the raw cancellation message says nothing an agent can act on.");
    }

    [TestMethod]
    public async Task RunAsync_WithNoDialogService_StillRunsTheActionAndStillMapsCancellation()
    {
        var ok = await ActionRunner.RunAsync(
            null, CancellationToken.None, _ => Task.FromResult(ToolResultHelper.Text("Clicked e5")));
        Assert.AreEqual("Clicked e5", TextOf(ok));

        var cancelled = await ActionRunner.RunAsync(
            null, CancellationToken.None, _ => Task.FromException<CallToolResult>(new OperationCanceledException()));
        StringAssert.Contains(TextOf(cancelled), "Timed out after");
    }

    [TestMethod]
    public async Task RunAsync_WhenTheActionFails_LetsTheFailureThrough()
    {
        var dialogs = new DialogService();
        dialogs.Subscribe(Page());

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => ActionRunner.RunAsync(
            dialogs, CancellationToken.None,
            _ => Task.FromException<CallToolResult>(new InvalidOperationException("no element"))));
    }
}
