using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class CoreToolsUnitTests
{
    private static AccessibilityNode Node(
        string role, string? name, long? backendId, params AccessibilityNode[] children)
        => new(
            NodeId: backendId?.ToString() ?? "x",
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: children,
            BackendDOMNodeId: backendId);

    private static AccessibilitySnapshot Snapshot(params AccessibilityNode[] roots)
        => new(roots, IgnoredCount: 0, DiagnosticMessage: null);

    private static string TextOf(CallToolResult result)
        => ((TextContentBlock)result.Content[0]).Text;

    // --- navigate ---

    [TestMethod]
    public async Task Navigate_Success_ReturnsOkAndRecordsUrl()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await CoreTools.NavigateAsync("https://example.com", service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("https://example.com", page.NavigatedUrl);
        StringAssert.Contains(TextOf(result), "https://example.com");
    }

    [TestMethod]
    public async Task Navigate_GotoThrows_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot()) { GotoError = new InvalidOperationException("boom") };
        var service = new FakeActivePageService(page);

        var result = await CoreTools.NavigateAsync("https://example.com", service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "boom");
    }

    [TestMethod]
    public async Task Navigate_InvalidatesSnapshot_SoLaterClickAsksForReSnapshot()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        await CoreTools.NavigateAsync("https://example.com", service, CancellationToken.None);
        var click = await CoreTools.ClickAsync("e1", null, service, CancellationToken.None);

        Assert.IsTrue(click.IsError);
        StringAssert.Contains(TextOf(click), "snapshot");
    }

    // --- snapshot ---

    [TestMethod]
    public async Task Snapshot_ReturnsAriaTextWithRefs()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "[ref=e1]");
    }

    [TestMethod]
    public async Task Snapshot_MaxDepthZero_RendersOnlyRoot()
    {
        var page = new FakeToolPage(Snapshot(Node("group", "A", 10, Node("button", "A1", 11))));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.SnapshotAsync(null, 0, service, CancellationToken.None);

        var text = TextOf(result);
        StringAssert.Contains(text, "- group \"A\"");
        Assert.IsFalse(text.Contains("A1"), "Depth 0 must not render children.");
    }

    [TestMethod]
    public async Task Snapshot_RootRef_ReRootsAtSubtree()
    {
        var page = new FakeToolPage(Snapshot(
            Node("group", "A", 10, Node("button", "A1", 11)),
            Node("group", "B", 20, Node("button", "B1", 21))));
        var service = new FakeActivePageService(page);

        // Full snapshot first to populate the ref map: e1=A, e2=A1, e3=B, e4=B1.
        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        var scoped = await CoreTools.SnapshotAsync("e3", null, service, CancellationToken.None);

        var text = TextOf(scoped);
        StringAssert.StartsWith(text, "- group \"B\"");
        Assert.IsFalse(text.Contains("A1"), "A subtree must not appear when rooted at B.");
    }

    [TestMethod]
    public async Task Snapshot_UnknownRootRef_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        var result = await CoreTools.SnapshotAsync("e999", null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }

    // --- click ---

    [TestMethod]
    public async Task Click_ResolvesRef_CallsClick()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        var result = await CoreTools.ClickAsync("e1", null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, page.RecordingLocator.ClickCount);
        Assert.AreEqual(0, page.RecordingLocator.DblClickCount);
        Assert.AreEqual(10, page.ResolvedBackendNodeId);
    }

    [TestMethod]
    public async Task Click_Double_CallsDblClick()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        await CoreTools.ClickAsync("e1", true, service, CancellationToken.None);

        Assert.AreEqual(1, page.RecordingLocator.DblClickCount);
        Assert.AreEqual(0, page.RecordingLocator.ClickCount);
    }

    [TestMethod]
    public async Task Click_NoSnapshot_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.ClickAsync("e1", null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "snapshot");
    }

    [TestMethod]
    public async Task Click_StaleRef_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        var result = await CoreTools.ClickAsync("e999", null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }

    // --- type ---

    [TestMethod]
    public async Task Type_Fills_ByDefault()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Name", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        var result = await CoreTools.TypeAsync("e1", "hello", null, null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("hello", page.RecordingLocator.FilledValue);
        Assert.IsNull(page.RecordingLocator.TypedValue);
        Assert.AreEqual(0, page.RecordingLocator.PressedKeys.Count);
    }

    [TestMethod]
    public async Task Type_Slowly_TypesCharacterByCharacter()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Name", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        await CoreTools.TypeAsync("e1", "hello", null, slowly: true, service, CancellationToken.None);

        Assert.AreEqual("hello", page.RecordingLocator.TypedValue);
        Assert.IsNull(page.RecordingLocator.FilledValue);
    }

    [TestMethod]
    public async Task Type_Submit_PressesEnterAfterFilling()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Name", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        await CoreTools.TypeAsync("e1", "hello", submit: true, null, service, CancellationToken.None);

        Assert.AreEqual("hello", page.RecordingLocator.FilledValue);
        CollectionAssert.AreEqual(new[] { "Enter" }, page.RecordingLocator.PressedKeys);
    }

    // --- screenshot ---

    [TestMethod]
    public async Task Screenshot_ReturnsPngImage()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await CoreTools.ScreenshotAsync(null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsInstanceOfType<ImageContentBlock>(result.Content[0]);
        Assert.AreEqual("image/png", ((ImageContentBlock)result.Content[0]).MimeType);
        Assert.AreEqual(false, page.ScreenshotFullPage);
    }

    [TestMethod]
    public async Task Screenshot_FullPage_PassesFlag()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        await CoreTools.ScreenshotAsync(true, service, CancellationToken.None);

        Assert.AreEqual(true, page.ScreenshotFullPage);
    }
}
