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

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.NavigateAsync("https://example.com", service, CancellationToken.None);
        var click = await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.IsTrue(click.IsError);
        StringAssert.Contains(TextOf(click), "snapshot");
    }

    // --- snapshot ---

    [TestMethod]
    public async Task Snapshot_ReturnsAriaTextWithRefs()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "[ref=e1]");
    }

    [TestMethod]
    public async Task Snapshot_MaxDepthZero_RendersOnlyRoot()
    {
        var page = new FakeToolPage(Snapshot(Node("group", "A", 10, Node("button", "A1", 11))));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: 0);

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
        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var scoped = await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: "e3",
            max_depth: null);

        var text = TextOf(scoped);
        StringAssert.StartsWith(text, "- group \"B\"");
        Assert.IsFalse(text.Contains("A1"), "A subtree must not appear when rooted at B.");
    }

    [TestMethod]
    public async Task Snapshot_UnknownRootRef_ReturnsError()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var result = await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: "e999",
            max_depth: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }

    // --- click ---

    [TestMethod]
    public async Task Click_ResolvesRef_CallsClick()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var result = await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

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

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: true);

        Assert.AreEqual(1, page.RecordingLocator.DblClickCount);
        Assert.AreEqual(0, page.RecordingLocator.ClickCount);
    }

    [TestMethod]
    public async Task Click_NoSnapshot_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "snapshot");
    }

    [TestMethod]
    public async Task Click_StaleRef_ReturnsGuidance()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var result = await CoreTools.ClickAsync(
            @ref: "e999",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e999");
    }

    [TestMethod]
    public async Task Click_WithASelector_ActsWithNoSnapshotTaken()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.ClickAsync(
            @ref: "#submit",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual(1, page.RecordingLocator.ClickCount);
        Assert.AreEqual("#submit", page.ResolvedSelector);
    }

    [TestMethod]
    public async Task Click_WithASelector_DoesNotAskForASnapshotAfterANavigation()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.NavigateAsync("https://example.com", service, CancellationToken.None);

        // Navigation drops the ref map, which is exactly the moment a selector earns its keep.
        var result = await CoreTools.ClickAsync(
            @ref: "text=Go",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual("text=Go", page.ResolvedSelector);
    }

    [TestMethod]
    public async Task Click_WithARefShapedTarget_IsNeverRunAsASelector()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var result = await CoreTools.ClickAsync(
            @ref: "e99",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "e99");
        Assert.IsNull(page.ResolvedSelector, "a stale ref is a stale ref, not a selector that matched nothing.");
    }

    [TestMethod]
    public async Task Click_WithAButtonAndModifiers_PassesThemToTheLocator()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var result = await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null,
            button: "right",
            modifiers: ["Control", "shift"]);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        var options = page.RecordingLocator.ClickOptions;
        Assert.IsNotNull(options, "a right-click has to reach the element, not just the coordinate tools.");
        Assert.AreEqual(MouseButton.Right, options.Button);
        Assert.AreEqual(KeyModifier.Control | KeyModifier.Shift, options.Modifiers);
    }

    [TestMethod]
    public async Task Click_WithNoButtonOrModifiers_StaysAPlainClick()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null,
            button: null,
            modifiers: null);

        Assert.AreEqual(1, page.RecordingLocator.ClickCount);
        Assert.IsNull(page.RecordingLocator.ClickOptions);
    }

    [TestMethod]
    public async Task Click_WithAnUnknownButtonOrModifier_SaysWhichValueIsWrong()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var badButton = await CoreTools.ClickAsync(
            @ref: "#submit",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null,
            button: "back");
        var badModifier = await CoreTools.ClickAsync(
            @ref: "#submit",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null,
            modifiers: ["Hyper"]);

        Assert.IsTrue(badButton.IsError);
        StringAssert.Contains(TextOf(badButton), "back");
        Assert.IsTrue(badModifier.IsError);
        StringAssert.Contains(TextOf(badModifier), "Hyper");
        Assert.AreEqual(0, page.RecordingLocator.ClickCount, "neither call should have reached the page.");
    }

    [TestMethod]
    public async Task Click_DoubleWithAButtonOrModifiers_IsRefusedRatherThanQuietlyPlain()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        var service = new FakeActivePageService(page);

        var result = await CoreTools.ClickAsync(
            @ref: "#submit",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: true,
            button: "right");

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "click_xy");
        Assert.AreEqual(0, page.RecordingLocator.DblClickCount);
    }

    // --- type ---

    [TestMethod]
    public async Task Type_Fills_ByDefault()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Name", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        var result = await CoreTools.TypeAsync(
            @ref: "e1",
            text: "hello",
            pageService: service,
            cancellationToken: CancellationToken.None,
            submit: null,
            slowly: null);

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

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.TypeAsync(
            @ref: "e1",
            text: "hello",
            pageService: service,
            cancellationToken: CancellationToken.None,
            submit: null,
            slowly: true);

        Assert.AreEqual("hello", page.RecordingLocator.TypedValue);
        Assert.IsNull(page.RecordingLocator.FilledValue);
    }

    [TestMethod]
    public async Task Type_Submit_PressesEnterAfterFilling()
    {
        var page = new FakeToolPage(Snapshot(Node("textbox", "Name", 10)));
        var service = new FakeActivePageService(page);

        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.TypeAsync(
            @ref: "e1",
            text: "hello",
            pageService: service,
            cancellationToken: CancellationToken.None,
            submit: true,
            slowly: null);

        Assert.AreEqual("hello", page.RecordingLocator.FilledValue);
        CollectionAssert.AreEqual(new[] { "Enter" }, page.RecordingLocator.PressedKeys);
    }

    // --- screenshot ---

    [TestMethod]
    public async Task Screenshot_ReturnsPngImage()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await CoreTools.ScreenshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            full_page: null);

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

        await CoreTools.ScreenshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            full_page: true);

        Assert.AreEqual(true, page.ScreenshotFullPage);
    }

    // --- navigate: the local filesystem ---

    [TestMethod]
    public async Task Navigate_ToAFileUrl_IsRefusedWithoutTouchingThePage()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);

        var result = await CoreTools.NavigateAsync("file:///etc/hosts", service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "file:// navigation is disabled");
        Assert.IsNull(page.NavigatedUrl);
    }

    [TestMethod]
    public async Task Navigate_ToAFileUrl_WithUnrestrictedFileAccess_IsAllowed()
    {
        var page = new FakeToolPage(Snapshot());
        var service = new FakeActivePageService(page);
        var unrestricted = new SecurityPolicy(new McpServerLaunchOptions { AllowUnrestrictedFileAccess = true });

        var result = await CoreTools.NavigateAsync(
            "file:///etc/hosts", service, CancellationToken.None, policy: unrestricted);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual("file:///etc/hosts", page.NavigatedUrl);
    }
}
