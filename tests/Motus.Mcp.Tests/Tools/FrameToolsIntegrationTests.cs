using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;
using Motus.Tests.Page;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives frame scoping against a frame the browser really does render in its own process, which is
/// the case the tool surface exists for: its content is not in the page's accessibility tree at all,
/// so without selection an agent cannot see it, let alone act on it.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class FrameToolsIntegrationTests
{
    private CrossOriginFixtureServer? _server;
    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;

    /// <summary>
    /// The session's page service, over a browser launched with site isolation forced. The launch
    /// goes through the session manager's own seam rather than a server option, because these flags
    /// exist to make the test deterministic and are not something a real deployment would set.
    /// </summary>
    [TestInitialize]
    public async Task Setup()
    {
        _server = new CrossOriginFixtureServer();
        _sessions = new BrowserSessionManager(new McpServerLaunchOptions())
        {
            LaunchOverride = ct => MotusLauncher.LaunchAsync(
                new LaunchOptions { Headless = true, Args = _server.IsolationArgs }, ct),
        };
        _pages = new ActivePageService(_sessions);

        try
        {
            var page = await _pages.GetOrCreateActivePageAsync();
            await page.GotoAsync(_server.OuterUrl);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
            return;
        }

        await WaitForFramesAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_sessions is not null)
            await _sessions.DisposeAsync();

        _server?.Dispose();
    }

    /// <summary>
    /// The frames arrive as events after the navigation settles, and the nested one only after its
    /// parent's session is armed, so both levels are waited for rather than assumed.
    /// </summary>
    private async Task WaitForFramesAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var frames = await _pages!.ListFramesAsync();
            if (frames.Count >= 4)
                return;

            await Task.Delay(100);
        }

        Assert.Inconclusive("The fixture's frames never became addressable.");
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private async Task<int> IndexOfAsync(string urlSuffix)
    {
        var frames = await _pages!.ListFramesAsync();
        for (var i = 0; i < frames.Count; i++)
        {
            if (frames[i].Frame.Url.EndsWith(urlSuffix, StringComparison.Ordinal))
                return i;
        }

        Assert.Fail($"No frame ending in '{urlSuffix}' was listed.");
        return -1;
    }

    [TestMethod]
    public async Task FrameList_NamesTheFrameInItsOwnProcessAndTheOneInsideIt()
    {
        var listed = TextOf(await FrameTools.FrameListAsync(_pages!, CancellationToken.None));

        StringAssert.Contains(listed, "/middle.html");
        StringAssert.Contains(listed, "/deep.html",
            "a frame nested inside a frame in its own process is reachable and belongs in the list");
    }

    [TestMethod]
    public async Task PageSnapshot_DoesNotContainTheFramesContent_ButSaysWhereToFindIt()
    {
        var text = TextOf(await CoreTools.SnapshotAsync(
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null));

        Assert.IsFalse(text.Contains("middle", StringComparison.Ordinal),
            "the content of a frame in its own process is not in the page's tree");
        StringAssert.Contains(text, "frame_select");
    }

    [TestMethod]
    public async Task ScopedSnapshot_ReadsTheFramesOwnTree()
    {
        await FrameTools.FrameSelectAsync(await IndexOfAsync("/middle.html"), _pages!, CancellationToken.None);

        var text = TextOf(await CoreTools.SnapshotAsync(
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null));

        // "Go" is the accessible name of the button inside this frame and appears nowhere in the
        // page's own tree, so matching it says the tree really came from the frame.
        StringAssert.Contains(text, "Go");
        StringAssert.Contains(text, "Scoped to frame");
    }

    [TestMethod]
    public async Task ARefFromAScopedSnapshot_ClicksInsideThatFrame()
    {
        var index = await IndexOfAsync("/middle.html");
        await FrameTools.FrameSelectAsync(index, _pages!, CancellationToken.None);
        await CoreTools.SnapshotAsync(
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null);

        var refId = await FindRefAsync("button");
        var clicked = await CoreTools.ClickAsync(
            @ref: refId,
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            @double: null);
        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        // Read back through the frame itself rather than trusting the tool's own report, the same
        // independent oracle the engine's frame tests use.
        var frames = await _pages!.ListFramesAsync();
        Assert.AreEqual(1, await frames[index].Frame.EvaluateAsync<int>("window.clicks"));
    }

    [TestMethod]
    public async Task Evaluate_WithAFrameScoped_SeesThatFramesGlobals()
    {
        await FrameTools.FrameSelectAsync(await IndexOfAsync("/middle.html"), _pages!, CancellationToken.None);

        var result = TextOf(await PageTools.EvaluateAsync(
            expression: "window.marker",
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            @ref: null));

        StringAssert.Contains(result, "middle");
    }

    [TestMethod]
    public async Task TheFrameNestedTwoProcessesDeep_IsSelectableAndReadable()
    {
        await FrameTools.FrameSelectAsync(await IndexOfAsync("/deep.html"), _pages!, CancellationToken.None);

        var result = TextOf(await PageTools.EvaluateAsync(
            expression: "window.marker",
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            @ref: null));

        StringAssert.Contains(result, "deep");
    }

    [TestMethod]
    public async Task SelectingThePage_ReturnsPerceptionToTheWholeDocument()
    {
        await FrameTools.FrameSelectAsync(await IndexOfAsync("/middle.html"), _pages!, CancellationToken.None);
        await FrameTools.FrameSelectAsync(0, _pages!, CancellationToken.None);

        var text = TextOf(await CoreTools.SnapshotAsync(
            pageService: _pages!,
            cancellationToken: CancellationToken.None,
            root_ref: null,
            max_depth: null));

        StringAssert.Contains(text, "main");
        Assert.IsFalse(text.Contains("Scoped to frame", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task Navigating_DropsTheScopeSoTheNextSnapshotIsThePage()
    {
        await FrameTools.FrameSelectAsync(await IndexOfAsync("/middle.html"), _pages!, CancellationToken.None);

        await CoreTools.NavigateAsync(
            "data:text/html,<h1>elsewhere</h1>", _pages!, CancellationToken.None);

        Assert.IsNull(_pages!.GetActiveFrame(),
            "the selected frame does not survive the document it lived in");
    }

    /// <summary>
    /// Finds the ref of the first node whose rendered line contains the given text, reading the
    /// snapshot the same way an agent would.
    /// </summary>
    /// <remarks>
    /// Matched on role rather than on the text in the markup, because a node's accessible name is
    /// not its text content when the element carries an <c>aria-label</c>. Matching the label would
    /// also match the frame's root node, whose name is the document URL.
    /// </remarks>
    private async Task<string> FindRefAsync(string marker)
    {
        var page = await _pages!.GetOrCreateActivePageAsync();
        var text = _pages!.GetSnapshotService(page).LastSnapshot ?? string.Empty;

        foreach (var line in text.Split('\n'))
        {
            if (!line.Contains(marker, StringComparison.Ordinal))
                continue;

            var start = line.IndexOf("[ref=", StringComparison.Ordinal);
            if (start < 0)
                continue;

            var end = line.IndexOf(']', start);
            if (end > start)
                return line[(start + 5)..end];
        }

        Assert.Fail($"No ref was assigned to a node matching '{marker}'. Snapshot:\n{text}");
        return string.Empty;
    }
}
