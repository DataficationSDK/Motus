using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Tests.Tools;

namespace Motus.Mcp.Tests.Snapshot;

/// <summary>
/// How a page snapshot prints the frames the page hosts, and where the refs it hands out inside
/// them point, on hand-built trees rather than a browser.
/// </summary>
[TestClass]
public class InlineFrameSnapshotTests
{
    [TestMethod]
    public async Task APageSnapshot_PrintsAFramesContentInsideTheElementThatHostsIt()
    {
        var page = PageWithOneFrame(out _);

        var text = await new PageSnapshotService(page).TakeSnapshotAsync();

        StringAssert.Contains(text, "- Iframe \"Payment frame\" [ref=e1] [frame=1]\n");
        StringAssert.Contains(text, "  - button \"Pay now\" [ref=f1e1]\n");
    }

    [TestMethod]
    public async Task ARefInsideAFrame_ResolvesThroughThatFrame()
    {
        var page = PageWithOneFrame(out var frame);
        var service = new PageSnapshotService(page);
        await service.TakeSnapshotAsync();

        service.ResolveRef("f1e1");

        Assert.AreEqual(42, frame.ResolvedBackendNodeId,
            "the element was read from the frame's document, so it is addressed through that frame");
        Assert.IsNull(page.ResolvedBackendNodeId,
            "addressing it through the page would resolve a node identifier the page never issued");
    }

    [TestMethod]
    public async Task ARefInsideAFrame_ResolvesThroughThatFrameEvenWithAnotherFrameSelected()
    {
        var page = PageWithOneFrame(out var frame);
        var service = new PageSnapshotService(page);
        await service.TakeSnapshotAsync();

        var elsewhere = ((FakeToolFrame)page.MainFrame).AddChild("http://example.test/other.html");
        service.ResolveRef("f1e1", elsewhere);

        Assert.AreEqual(42, frame.ResolvedBackendNodeId);
        Assert.IsNull(elsewhere.ResolvedBackendNodeId,
            "a ref names the document it came from; only a selector means whatever is scoped now");
    }

    [TestMethod]
    public async Task TheRefsOfThePageItself_AreUnchangedByTheFramesInsideIt()
    {
        var page = PageWithOneFrame(out _);

        var text = await new PageSnapshotService(page).TakeSnapshotAsync();

        // The page numbers its own refs as it always did, and the frame numbers from one again, so
        // the numbers in the page do not shift when a frame gains or loses an element.
        StringAssert.Contains(text, "[ref=e1]");
        Assert.IsFalse(text.Contains("[ref=e2]", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task WithNoRoomForFrames_TheFrameIsLeftOutAndSaidSo()
    {
        var page = PageWithOneFrame(out _);

        var text = await new PageSnapshotService(page)
            .TakeSnapshotAsync(scope: null, rootRef: null, maxDepth: null, maxFrames: 0);

        Assert.IsFalse(text.Contains("Pay now", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "1 more frame whose contents are not in this tree");
    }

    [TestMethod]
    public async Task ASnapshotScopedToAFrame_HandsOutPlainRefs()
    {
        var page = PageWithOneFrame(out var frame);

        var text = await new PageSnapshotService(page)
            .TakeSnapshotAsync(scope: frame, rootRef: null, maxDepth: null);

        StringAssert.Contains(text, "- button \"Pay now\" [ref=e1]\n");
        Assert.IsFalse(text.Contains("f1e", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task EachFrame_NumbersItsOwnRefsBehindItsOwnIndex()
    {
        var page = new FakeToolPage(Document(
            Node("Iframe", "First", 1),
            Node("Iframe", "Second", 2)));

        var main = new FakeToolFrame(page, "http://example.test/");
        page.FrameTree = main;
        main.AddChild("http://example.test/a.html").Snapshot = Document(Node("button", "A", 10));
        main.AddChild("http://example.test/b.html").Snapshot = Document(Node("button", "B", 20));

        var text = await new PageSnapshotService(page).TakeSnapshotAsync();

        StringAssert.Contains(text, "- button \"A\" [ref=f1e1]\n");
        StringAssert.Contains(text, "- button \"B\" [ref=f2e1]\n");
    }

    /// <summary>
    /// A page whose only content is an element hosting one frame, and that frame's own document.
    /// </summary>
    private static FakeToolPage PageWithOneFrame(out FakeToolFrame frame)
    {
        var page = new FakeToolPage(Document(Node("Iframe", "Payment frame", 7)));
        var main = new FakeToolFrame(page, "http://example.test/");
        page.FrameTree = main;

        frame = main.AddChild("http://example.test/frame.html");
        frame.Snapshot = Document(Node("button", "Pay now", 42));
        return page;
    }

    private static AccessibilitySnapshot Document(params AccessibilityNode[] children)
        => new(
            [new AccessibilityNode("root", "RootWebArea", null, null, null,
                new Dictionary<string, string?>(), children, BackendDOMNodeId: 1)],
            IgnoredCount: 0,
            DiagnosticMessage: null);

    private static AccessibilityNode Node(string role, string name, long backendNodeId)
        => new(backendNodeId.ToString(), role, name, null, null,
            new Dictionary<string, string?>(), [], BackendDOMNodeId: backendNodeId);
}
