using System.Diagnostics;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Tests.Fixtures;

namespace Motus.Mcp.Tests.Snapshot;

/// <summary>
/// Drives the bench page's payment frame the way an agent does: one snapshot of the page, then one
/// click on something inside the frame. The frame is rendered in the page's own process, which is
/// the case where the browser's own tree is most misleading, because it describes the element that
/// hosts the frame and nothing that is inside it.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class InlineFrameIntegrationTests
{
    private McpBenchFixtureServer? _server;
    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new McpBenchFixtureServer();
        _sessions = new BrowserSessionManager(new McpServerLaunchOptions { Headless = true });
        _pages = new ActivePageService(_sessions);

        try
        {
            var page = await _pages.GetOrCreateActivePageAsync();
            await page.GotoAsync(_server.IndexUrl);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _pages?.Shutdown();
        if (_sessions is not null)
            await _sessions.DisposeAsync();

        _server?.Dispose();
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private Task<CallToolResult> SnapshotAsync(int? maxDepth = null, int? maxFrames = null)
        => CoreTools.SnapshotAsync(
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: maxDepth,
            max_frames: maxFrames);

    [TestMethod]
    public async Task ThePageSnapshot_ContainsTheButtonInsideTheFrame()
    {
        var text = TextOf(await SnapshotAsync());

        StringAssert.Contains(text, "- Iframe \"Payment frame\"");
        StringAssert.Contains(text, "[frame=1]",
            "the element that hosts a frame says which frame, so f1 can be read back to it");
        StringAssert.Contains(text, "- button \"Pay now\" [ref=f1e1]");
        Assert.IsFalse(text.Contains("more frames whose contents", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task ARefInsideTheFrame_ClicksInsideTheFrame_WithNoFrameSelected()
    {
        await SnapshotAsync();

        var clicked = await CoreTools.ClickAsync(
            @ref: "f1e1",
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            @double: null);
        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        // Read back through the frame itself rather than trusting the tool's own report.
        var frames = await _pages!.ListFramesAsync();
        var text = await frames[1].Frame.EvaluateAsync<string>("document.querySelector('button').textContent");
        Assert.AreEqual("Paid!", text);
    }

    [TestMethod]
    public async Task ClickingInsideAFrame_IsAsQuickAsClickingThePage()
    {
        await SnapshotAsync();

        var stopwatch = Stopwatch.StartNew();
        var clicked = await CoreTools.ClickAsync(
            @ref: "f1e1",
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            @double: null);
        stopwatch.Stop();

        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        // The click itself takes about a tenth of a second. The bound is many times that so a
        // loaded machine does not fail the run, and it still catches the retry loop that once made
        // a click inside a frame take five and a half seconds.
        Assert.IsTrue(
            stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"a click inside a frame took {stopwatch.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public async Task WithNoRoomForFrames_ThePageSaysWhatItLeftOut()
    {
        var text = TextOf(await SnapshotAsync(maxFrames: 0));

        Assert.IsFalse(text.Contains("Pay now", StringComparison.Ordinal), text);
        StringAssert.Contains(text, "1 more frame whose contents are not in this tree");
        StringAssert.Contains(text, "frame_select");
    }

    [TestMethod]
    public async Task ADepthLimit_StopsAtTheFrameBoundaryLikeAnywhereElse()
    {
        // The element that hosts the frame is two levels below the document, so a limit of two
        // reaches it and stops before the document it holds.
        var text = TextOf(await SnapshotAsync(maxDepth: 2));

        StringAssert.Contains(text, "- Iframe \"Payment frame\"");
        Assert.IsFalse(text.Contains("Pay now", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task SelectingTheFrame_StillDescribesItOnItsOwnWithPlainRefs()
    {
        await FrameTools.FrameSelectAsync(1, _pages!, CancellationToken.None);

        var text = TextOf(await SnapshotAsync());

        StringAssert.Contains(text, "Scoped to frame");
        StringAssert.Contains(text, "- button \"Pay now\" [ref=e2]");
    }

    [TestMethod]
    public async Task RootingAtARefInsideAFrame_DescribesThatFrame()
    {
        await SnapshotAsync();

        var text = TextOf(await CoreTools.SnapshotAsync(
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            root_ref: "f1e1",
            max_depth: null,
            max_frames: null));

        StringAssert.Contains(text, "Scoped to frame");
        StringAssert.Contains(text, "- button \"Pay now\" [ref=e1]");
    }

    [TestMethod]
    public async Task AFrameThatNavigates_IsReportedByTheNextAction()
    {
        await SnapshotAsync();

        var frames = await _pages!.ListFramesAsync();
        await frames[1].Frame.GotoAsync(_server!.OtherUrl);

        // Any action will do; the click is on the page rather than in the frame, so the row comes
        // from the frame having moved rather than from the click failing.
        var clicked = await CoreTools.ClickAsync(
            @ref: "#late-btn",
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            @double: null);

        StringAssert.Contains(TextOf(clicked), "Refs inside frame 1 no longer address anything");
    }
}
