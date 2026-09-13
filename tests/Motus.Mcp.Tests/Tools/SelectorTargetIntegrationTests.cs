using ModelContextProtocol.Protocol;
using Motus.Mcp;
using Motus.Mcp.Tests.Fixtures;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Acting on a page by selector rather than by ref, and clicking it with a button other than the
/// left one, against a real browser. Both are the cases an agent reaches for when it already knows
/// the element it wants: after a navigation, when the refs it holds are stale, or when the page
/// offers something only a right-click reveals.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class SelectorTargetIntegrationTests
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

    [TestMethod]
    public async Task Click_BySelector_WorksWithNoSnapshotTaken()
    {
        var ct = CancellationToken.None;

        // No snapshot in this test at all: a selector says which element it means, so there is
        // nothing for the session to have read first.
        var click = await CoreTools.ClickAsync("#late-btn", _pages!, ct, @double: null);

        Assert.IsFalse(click.IsError ?? false, TextOf(click));

        // The handler appends a button a second and a half later, which is proof the click landed
        // on the element rather than merely being accepted.
        Assert.IsTrue(
            await EventuallyAsync("!!document.body && document.body.innerText.includes('Late button')"),
            "the clicked handler should have added its button to the page.");
    }

    [TestMethod]
    public async Task Click_WithTheRightButton_RaisesAContextMenuEvent()
    {
        var ct = CancellationToken.None;

        var click = await CoreTools.ClickAsync(
            "#c", _pages!, ct, @double: null, button: "right");

        Assert.IsFalse(click.IsError ?? false, TextOf(click));

        Assert.IsTrue(
            await EventuallyAsync("window.contextMenuCount > 0"),
            "a right-click on the canvas should have raised a contextmenu event.");

        var evaluated = await PageTools.EvaluateAsync(
            expression: "window.contextMenuCount",
            pageService: _pages!,
            cancellationToken: ct,
            @ref: null);

        Assert.IsFalse(evaluated.IsError ?? false, TextOf(evaluated));
        Assert.IsNotNull(evaluated.StructuredContent);
        Assert.AreEqual(1, evaluated.StructuredContent.Value.GetProperty("result").GetInt32());
    }

    /// <summary>
    /// Polls an expression until it is true or a couple of seconds pass. The page reacts to a click
    /// on its own schedule, so a single read straight after one is a race.
    /// </summary>
    private async Task<bool> EventuallyAsync(string expression)
    {
        var page = await _pages!.GetOrCreateActivePageAsync();
        for (var attempt = 0; attempt < 60; attempt++)
        {
            if (await page.EvaluateAsync<bool>(expression))
                return true;

            await Task.Delay(50);
        }

        return false;
    }

    private static string TextOf(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));
}
