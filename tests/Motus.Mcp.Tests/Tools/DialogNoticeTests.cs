using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// The line every tool result carries while a dialog waits to be answered. It is added to results
/// the tools have already produced, so an agent that moved on to something else is told why the
/// page is not moving, without the tools having to know about dialogs at all.
/// </summary>
[TestClass]
public class DialogNoticeTests
{
    private static FakeToolPage Page() => new(new AccessibilitySnapshot([], 0, null));

    private static string TextOf(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static DialogService WithPendingDialog(DialogType type, string message)
    {
        var service = new DialogService();
        var page = Page();
        service.Subscribe(page);
        page.RaiseDialog(new FakeDialog(type, message));
        return service;
    }

    [TestMethod]
    public void Prefix_PutsTheNoticeInFrontOfASnapshot()
    {
        var dialogs = WithPendingDialog(DialogType.Confirm, "Delete this?");
        var snapshot = ToolResultHelper.Text("- document\n  - button \"Delete account\" [ref=e5]");

        var prefixed = DialogNotice.Prefix(dialogs, snapshot);

        Assert.AreEqual(2, prefixed.Content.Count);
        Assert.AreEqual(
            "A \"confirm\" dialog is open: \"Delete this?\". Handle it with handle_dialog before other actions.",
            ((TextContentBlock)prefixed.Content[0]).Text);
        StringAssert.Contains(TextOf(prefixed), "button \"Delete account\" [ref=e5]");
    }

    [TestMethod]
    public void Prefix_LeavesTheDialogPendingForHandleDialog()
    {
        var dialogs = WithPendingDialog(DialogType.Alert, "Are you sure?");

        DialogNotice.Prefix(dialogs, ToolResultHelper.Text("anything"));

        Assert.IsNotNull(dialogs.TakePendingDialog());
    }

    [TestMethod]
    public void Prefix_AfterTheDialogIsHandled_ChangesNothing()
    {
        var dialogs = WithPendingDialog(DialogType.Alert, "Are you sure?");
        dialogs.TakePendingDialog();

        var snapshot = ToolResultHelper.Text("- document");
        var prefixed = DialogNotice.Prefix(dialogs, snapshot);

        Assert.AreSame(snapshot, prefixed);
        Assert.AreEqual("- document", TextOf(prefixed));
    }

    [TestMethod]
    public void Prefix_WithNoDialogService_ChangesNothing()
    {
        var snapshot = ToolResultHelper.Text("- document");
        Assert.AreSame(snapshot, DialogNotice.Prefix(null, snapshot));
    }

    [TestMethod]
    public void Prefix_DoesNotRepeatItselfOnTheResultThatAlreadyReportedTheDialog()
    {
        var dialogs = WithPendingDialog(DialogType.Alert, "Are you sure?");
        var interrupted = ToolResultHelper.Text(DialogNotice.Interrupted(dialogs.PeekPendingDialog()!));

        var prefixed = DialogNotice.Prefix(dialogs, interrupted);

        Assert.AreSame(interrupted, prefixed);
    }

    [TestMethod]
    public void Prefix_KeepsStructuredContentAndTheErrorFlag()
    {
        var dialogs = WithPendingDialog(DialogType.Prompt, "Your name?");
        using var document = JsonDocument.Parse("{\"result\":12}");
        var structured = ToolResultHelper.Structured(document.RootElement.Clone());

        var prefixed = DialogNotice.Prefix(dialogs, structured);
        Assert.IsNotNull(prefixed.StructuredContent);
        StringAssert.Contains(TextOf(prefixed), "\"prompt\" dialog is open");

        var failed = DialogNotice.Prefix(dialogs, ToolResultHelper.Error("Click failed: no element"));
        Assert.IsTrue(failed.IsError ?? false);
    }

    [TestMethod]
    public void Interrupted_NamesTheDialogTypeAndMessage()
    {
        Assert.AreEqual(
            "The action opened an alert dialog: \"Are you sure?\". Call handle_dialog to accept or dismiss it. The page finishes the action once the dialog is answered, so a dialog opened from a mousedown handler means the click lands after this call returned.",
            DialogNotice.Interrupted(new FakeDialog(DialogType.Alert, "Are you sure?")));
        Assert.AreEqual(
            "The action opened a confirm dialog: \"Delete this?\". Call handle_dialog to accept or dismiss it. The page finishes the action once the dialog is answered, so a dialog opened from a mousedown handler means the click lands after this call returned.",
            DialogNotice.Interrupted(new FakeDialog(DialogType.Confirm, "Delete this?")));
        Assert.AreEqual(
            "The action opened a beforeunload dialog: \"\". Call handle_dialog to accept or dismiss it. The page finishes the action once the dialog is answered, so a dialog opened from a mousedown handler means the click lands after this call returned.",
            DialogNotice.Interrupted(new FakeDialog(DialogType.BeforeUnload, "")));
    }
}
