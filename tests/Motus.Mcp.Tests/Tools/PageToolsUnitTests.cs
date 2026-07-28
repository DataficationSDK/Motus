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
        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: Ct,
            root_ref: null,
            max_depth: null);
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
        var afterReload = await PageTools.EvaluateAsync(
            expression: "x",
            pageService: service,
            cancellationToken: Ct,
            @ref: "e1");

        Assert.IsTrue(afterReload.IsError);
        StringAssert.Contains(TextOf(afterReload), "snapshot");
    }

    // --- dialog ---

    [TestMethod]
    public async Task HandleDialog_NoPendingDialog_ReturnsError()
    {
        var result = await PageTools.HandleDialogAsync(
            accept: true,
            dialogService: new DialogService(),
            cancellationToken: Ct,
            text: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "No dialog");
    }

    [TestMethod]
    public async Task HandleDialog_Accept_CallsAccept()
    {
        var (dialogService, dialog) = PendingDialog(new FakeDialog(DialogType.Confirm, "Sure?"));

        var result = await PageTools.HandleDialogAsync(
            accept: true,
            dialogService: dialogService,
            cancellationToken: Ct,
            text: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(dialog.Accepted);
        Assert.IsNull(dialog.AcceptedText);
    }

    [TestMethod]
    public async Task HandleDialog_AcceptPrompt_PassesText()
    {
        var (dialogService, dialog) = PendingDialog(new FakeDialog(DialogType.Prompt, "Name?"));

        var result = await PageTools.HandleDialogAsync(
            accept: true,
            dialogService: dialogService,
            cancellationToken: Ct,
            text: "Ada");

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("Ada", dialog.AcceptedText);
    }

    [TestMethod]
    public async Task HandleDialog_Dismiss_CallsDismiss()
    {
        var (dialogService, dialog) = PendingDialog(new FakeDialog(DialogType.Confirm, "Sure?"));

        var result = await PageTools.HandleDialogAsync(
            accept: false,
            dialogService: dialogService,
            cancellationToken: Ct,
            text: null);

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

        var result = await PageTools.EvaluateAsync(
            expression: "1 + 41",
            pageService: service,
            cancellationToken: Ct,
            @ref: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("1 + 41", page.EvaluatedExpression);
        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(42, result.StructuredContent.Value.GetProperty("result").GetInt32());
        Assert.AreEqual("{\"result\":42}", TextOf(result));
    }

    [TestMethod]
    public async Task Evaluate_ScalarResult_IsWrappedInAnObject()
    {
        // Structured content is an object, so a scalar result has to arrive as a field of one
        // or the client rejects the reply before the model reads it.
        var page = new FakeToolPage(Snapshot()) { EvaluateReturn = Json("\"ready\"") };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync(
            expression: "document.readyState",
            pageService: service,
            cancellationToken: Ct,
            @ref: null);

        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(JsonValueKind.Object, result.StructuredContent.Value.ValueKind);
        Assert.AreEqual("ready", result.StructuredContent.Value.GetProperty("result").GetString());
    }

    [TestMethod]
    public async Task Evaluate_ObjectResult_KeepsItsShapeUnderResult()
    {
        var page = new FakeToolPage(Snapshot()) { EvaluateReturn = Json("{\"cells\":3,\"names\":[\"a\",\"b\"]}") };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync(
            expression: "({ cells: 3, names: ['a', 'b'] })",
            pageService: service,
            cancellationToken: Ct,
            @ref: null);

        Assert.IsNotNull(result.StructuredContent);
        var value = result.StructuredContent.Value.GetProperty("result");
        Assert.AreEqual(3, value.GetProperty("cells").GetInt32());
        Assert.AreEqual(2, value.GetProperty("names").GetArrayLength());
    }

    [TestMethod]
    public async Task Evaluate_ArrayResult_IsWrappedInAnObject()
    {
        var page = new FakeToolPage(Snapshot()) { EvaluateReturn = Json("[1,2,3]") };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync(
            expression: "[1, 2, 3]",
            pageService: service,
            cancellationToken: Ct,
            @ref: null);

        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(JsonValueKind.Object, result.StructuredContent.Value.ValueKind);
        Assert.AreEqual(3, result.StructuredContent.Value.GetProperty("result").GetArrayLength());
    }

    [TestMethod]
    public async Task Evaluate_NoValue_ReturnsNullResult()
    {
        // An expression yielding undefined leaves a default JsonElement, which has no JSON form.
        var page = new FakeToolPage(Snapshot()) { EvaluateReturn = default };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync(
            expression: "window.setTitle()",
            pageService: service,
            cancellationToken: Ct,
            @ref: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual(JsonValueKind.Null, result.StructuredContent.Value.GetProperty("result").ValueKind);
        Assert.AreEqual("{\"result\":null}", TextOf(result));
    }

    [TestMethod]
    public async Task Evaluate_WithRef_RunsAgainstTheElement()
    {
        var (page, service) = await SnapshottedAsync();
        page.RecordingLocator.ElementEvaluateReturn = Json("\"hello\"");

        var result = await PageTools.EvaluateAsync(
            expression: "el => el.textContent",
            pageService: service,
            cancellationToken: Ct,
            @ref: "e1");

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("el => el.textContent", page.RecordingLocator.EvaluatedElementExpression);
        Assert.IsNotNull(result.StructuredContent);
        Assert.AreEqual("hello", result.StructuredContent.Value.GetProperty("result").GetString());
    }

    [TestMethod]
    public async Task Evaluate_WithRef_NoSnapshot_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Field", 10)));
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync(
            expression: "el => el.value",
            pageService: service,
            cancellationToken: Ct,
            @ref: "e1");

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "snapshot");
    }

    [TestMethod]
    public async Task Evaluate_WithRef_StaleRef_ReturnsGuidance()
    {
        var (_, service) = await SnapshottedAsync();

        var result = await PageTools.EvaluateAsync(
            expression: "el => el.value",
            pageService: service,
            cancellationToken: Ct,
            @ref: "e999");

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }

    [TestMethod]
    public async Task Evaluate_ScriptError_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot()) { EvaluateError = new InvalidOperationException("boom") };
        var service = new FakeActivePageService(page);

        var result = await PageTools.EvaluateAsync(
            expression: "nope()",
            pageService: service,
            cancellationToken: Ct,
            @ref: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "boom");
    }
}
