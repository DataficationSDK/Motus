using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class PageToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static AccessibilityNode Node(string role, string? name, long? backendId)
        => new(
            NodeId: backendId?.ToString() ?? "x",
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: backendId);

    private static AccessibilitySnapshot Snapshot(params AccessibilityNode[] roots)
        => new(roots, IgnoredCount: 0, DiagnosticMessage: null);

    /// <summary>Builds a service over a one-element page and snapshots it so e1 resolves.</summary>
    private static async Task<(FakeToolPage page, FakeActivePageService service)> SnapshottedAsync()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Field", 10)));
        var service = new FakeActivePageService(page);
        await CoreTools.SnapshotAsync(null, null, service, Ct);
        return (page, service);
    }

    // --- history ---

    [TestMethod]
    public async Task GoBack_NoHistory_ReturnsInformativeText()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await PageTools.GoBackAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(page.GoBackCalled);
        StringAssert.Contains(TextOf(result), "No previous history entry");
    }

    [TestMethod]
    public async Task GoForward_NoHistory_ReturnsInformativeText()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await PageTools.GoForwardAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(page.GoForwardCalled);
        StringAssert.Contains(TextOf(result), "No next history entry");
    }

    [TestMethod]
    public async Task Reload_ReportsTheUrl()
    {
        var page = new FakeToolPage(Snapshot()) { PageUrl = "https://x.test" };
        var service = new FakeActivePageService(page);

        var result = await PageTools.ReloadAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(page.ReloadCalled);
        StringAssert.Contains(TextOf(result), "https://x.test");
    }

    [TestMethod]
    public async Task Reload_InvalidatesSnapshot()
    {
        var (_, service) = await SnapshottedAsync();

        await PageTools.ReloadAsync(service, Ct);
        var afterReload = await PageTools.EvaluateAsync("x", "e1", service, Ct);

        Assert.IsTrue(afterReload.IsError);
        StringAssert.Contains(TextOf(afterReload), "snapshot");
    }

    // --- dialog ---

    [TestMethod]
    public async Task HandleDialog_NoPendingDialog_ReturnsError()
    {
        var result = await PageTools.HandleDialogAsync(true, null, new DialogService(), Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "No dialog");
    }

    [TestMethod]
    public async Task HandleDialog_Accept_CallsAccept()
    {
        var (dialogService, dialog) = PendingDialog(new FakeDialog(DialogType.Confirm, "Sure?"));

        var result = await PageTools.HandleDialogAsync(true, null, dialogService, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(dialog.Accepted);
        Assert.IsNull(dialog.AcceptedText);
    }

    [TestMethod]
    public async Task HandleDialog_AcceptPrompt_PassesText()
    {
        var (dialogService, dialog) = PendingDialog(new FakeDialog(DialogType.Prompt, "Name?"));

        var result = await PageTools.HandleDialogAsync(true, "Ada", dialogService, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("Ada", dialog.AcceptedText);
    }

    [TestMethod]
    public async Task HandleDialog_Dismiss_CallsDismiss()
    {
        var (dialogService, dialog) = PendingDialog(new FakeDialog(DialogType.Confirm, "Sure?"));

        var result = await PageTools.HandleDialogAsync(false, null, dialogService, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(dialog.Dismissed);
    }

    private static (DialogService service, FakeDialog dialog) PendingDialog(FakeDialog dialog)
    {
        var page = new FakeToolPage(Snapshot());
        var service = new DialogService();
        service.Subscribe(page);
        page.RaiseDialog(dialog);
        return (service, dialog);
    }

    // --- evaluate ---

    [TestMethod]
    public async Task Evaluate_PageLevel_ReturnsStructuredContentAndMatchingText()
    {
        var page = new FakeToolPage(Snapshot()) { EvaluateReturn = Json("42") };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync("1 + 41", null, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("1 + 41", page.EvaluatedExpression);
        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(42, result.StructuredContent.Value.GetInt32());
        Assert.AreEqual("42", TextOf(result));
    }

    [TestMethod]
    public async Task Evaluate_WithRef_RunsAgainstTheElement()
    {
        var (page, service) = await SnapshottedAsync();
        page.RecordingLocator.ElementEvaluateReturn = Json("\"hello\"");

        var result = await PageTools.EvaluateAsync("el => el.textContent", "e1", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("el => el.textContent", page.RecordingLocator.EvaluatedElementExpression);
        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(JsonValueKind.String, result.StructuredContent.Value.ValueKind);
    }

    [TestMethod]
    public async Task Evaluate_WithRef_NoSnapshot_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Field", 10)));
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync("el => el.value", "e1", service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "snapshot");
    }

    [TestMethod]
    public async Task Evaluate_WithRef_StaleRef_ReturnsGuidance()
    {
        var (_, service) = await SnapshottedAsync();

        var result = await PageTools.EvaluateAsync("el => el.value", "e999", service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }

    [TestMethod]
    public async Task Evaluate_ScriptError_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot()) { EvaluateError = new InvalidOperationException("boom") };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync("nope()", null, service, Ct);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "boom");
    }
}
