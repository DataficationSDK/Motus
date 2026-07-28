using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Covers frame selection and what it scopes, against a fake page rather than a browser: which
/// document a snapshot reads, which frame a ref resolves through, and when the scope is dropped.
/// </summary>
[TestClass]
public class FrameToolsUnitTests
{
    /// <summary>
    /// A page whose main frame holds one child, each with a distinct accessibility tree, so a
    /// snapshot taken from the wrong one is visible in the text rather than merely suspected.
    /// </summary>
    private static (FakeActivePageService Service, FakeToolPage Page, FakeToolFrame Child) PageWithAFrame()
    {
        var page = new FakeToolPage(Tree("page heading"))
        {
            PageUrl = "https://example.test/outer",
        };

        var main = new FakeToolFrame(page, page.PageUrl);
        var child = main.AddChild("https://other.test/inner");
        child.Name = "inner";
        child.Snapshot = Tree("frame heading");
        page.FrameTree = main;

        return (new FakeActivePageService(page), page, child);

        static AccessibilitySnapshot Tree(string name) => new(
            [new AccessibilityNode("1", "heading", name, null, null,
                new Dictionary<string, string?>(), [], BackendDOMNodeId: 42, Ignored: false)],
            IgnoredCount: 0,
            DiagnosticMessage: null);
    }

    private static string TextOf(ModelContextProtocol.Protocol.CallToolResult result)
        => string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(b => b.Text));

    [TestMethod]
    public async Task FrameList_NumbersThePageFirstAndMarksTheScope()
    {
        var (service, _, _) = PageWithAFrame();

        var listed = TextOf(await FrameTools.FrameListAsync(service, CancellationToken.None));

        StringAssert.Contains(listed, "[0]");
        StringAssert.Contains(listed, "https://example.test/outer");
        StringAssert.Contains(listed, "[1]");
        StringAssert.Contains(listed, "https://other.test/inner");
        StringAssert.Contains(listed, "inner");
        StringAssert.StartsWith(listed, "* ", "with nothing selected the page itself is the scope");
    }

    [TestMethod]
    public async Task FrameSelect_OutOfRange_ReportsTheRangeRatherThanThrowing()
    {
        var (service, _, _) = PageWithAFrame();

        var result = await FrameTools.FrameSelectAsync(7, service, CancellationToken.None);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "frame_list");
    }

    [TestMethod]
    public async Task Snapshot_AfterSelectingAFrame_ReadsThatFramesTree()
    {
        var (service, _, _) = PageWithAFrame();

        var before = TextOf(await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null));
        StringAssert.Contains(before, "page heading");

        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);
        var after = TextOf(await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null));

        StringAssert.Contains(after, "frame heading");
        Assert.IsFalse(after.Contains("page heading", StringComparison.Ordinal),
            "a scoped snapshot describes that frame's document, not the page's");
    }

    [TestMethod]
    public async Task Snapshot_OfAPageWithFrames_SaysWhereTheRestOfTheContentIs()
    {
        var (service, _, _) = PageWithAFrame();

        var text = TextOf(await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null));

        // Without this an agent reads an iframe element with nothing under it and concludes the
        // content is missing, which is the whole failure this hint exists to prevent.
        StringAssert.Contains(text, "frame_select");
    }

    [TestMethod]
    public async Task Ref_FromAScopedSnapshot_ResolvesThroughThatFrame()
    {
        var (service, page, child) = PageWithAFrame();

        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);
        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.AreEqual(42, child.ResolvedBackendNodeId,
            "a backend node id only means anything on the session that reported it");
        Assert.IsNull(page.ResolvedBackendNodeId,
            "resolving through the page would address a different document, or nothing");
    }

    [TestMethod]
    public async Task Ref_FromAScopedSnapshot_KeepsResolvingAfterTheScopeMovesBack()
    {
        var (service, page, child) = PageWithAFrame();

        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);
        await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);
        await FrameTools.FrameSelectAsync(0, service, CancellationToken.None);

        await CoreTools.ClickAsync(
            @ref: "e1",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @double: null);

        Assert.AreEqual(42, child.ResolvedBackendNodeId);
        Assert.IsNull(page.ResolvedBackendNodeId);
    }

    [TestMethod]
    public async Task Evaluate_WithNoRef_RunsInTheScopedFrame()
    {
        var (service, page, child) = PageWithAFrame();

        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);
        await PageTools.EvaluateAsync(
            expression: "window.marker",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @ref: null);

        CollectionAssert.Contains(child.Evaluated, "window.marker");
        Assert.IsNull(page.EvaluatedExpression);
    }

    [TestMethod]
    public async Task WaitForText_RunsInTheScopedFrame()
    {
        var (service, page, child) = PageWithAFrame();

        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);
        await InteractionTools.WaitForAsync(
            pageService: service,
            cancellationToken: CancellationToken.None,
            time: null,
            text: "ready",
            text_gone: null);

        Assert.AreEqual(1, child.WaitedFunctions.Count,
            "text inside a frame is not in the page's document, so a page-level wait would time out");
        Assert.AreEqual(0, page.WaitedFunctions.Count);
    }

    [TestMethod]
    public async Task Navigating_DropsTheScope()
    {
        var (service, _, _) = PageWithAFrame();
        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);
        Assert.IsNotNull(service.GetActiveFrame());

        await CoreTools.NavigateAsync("https://example.test/next", service, CancellationToken.None);

        Assert.IsNull(service.GetActiveFrame(),
            "the frame that was selected does not survive the document it lived in");
    }

    [TestMethod]
    public async Task ADetachedFrame_IsDroppedRatherThanHandedBack()
    {
        var (service, _, child) = PageWithAFrame();
        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);

        child.IsDetached = true;

        Assert.IsNull(service.GetActiveFrame(),
            "otherwise the removal surfaces as an unrelated protocol error on some later call");
    }

    [TestMethod]
    public async Task SelectingThePage_ClearsTheScope()
    {
        var (service, _, _) = PageWithAFrame();
        await FrameTools.FrameSelectAsync(1, service, CancellationToken.None);

        await FrameTools.FrameSelectAsync(0, service, CancellationToken.None);

        Assert.IsNull(service.GetActiveFrame());
    }
}
