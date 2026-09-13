using ModelContextProtocol.Protocol;
using Motus.Mcp;
using Motus.Mcp.Tests.Fixtures;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// What an action tells the agent, in a real browser, on the three pages' worth of behaviour that
/// an agent has to notice: a button that logs an error and throws, a link that opens a tab, and a
/// form submission that does neither.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class ActionReportIntegrationTests
{
    private McpBenchFixtureServer? _server;
    private BrowserSessionManager? _sessions;
    private ConsoleService? _console;
    private ActivePageService? _pages;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new McpBenchFixtureServer();
        _sessions = new BrowserSessionManager(new McpServerLaunchOptions { Headless = true });
        _console = new ConsoleService();
        _pages = new ActivePageService(_sessions, dialogService: null, _console);

        try
        {
            await _pages.GetOrCreateActivePageAsync();
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
    public async Task ClickingTheButtonThatBreaksThings_CountsTheErrors_AndGivesACursorThatReadsThem()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        var snapshot = await OpenBenchAsync();
        var clicked = await CoreTools.ClickAsync(RefFor(snapshot, "Break things"), pages, ct);
        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        var text = TextOf(clicked);
        var console = text.Split('\n').SingleOrDefault(line => line.StartsWith("Console:", StringComparison.Ordinal))
            ?? throw new AssertFailedException($"The click reported no console errors. Result was:\n{text}");

        StringAssert.Contains(console, "1 error");
        StringAssert.Contains(console, "1 page error");

        // The cursor the row printed reads exactly what the click logged.
        var since = long.Parse(console[(console.LastIndexOf("since=", StringComparison.Ordinal) + 6)..].TrimEnd('.'));
        var entries = TextOf(ConsoleTools.ConsoleMessages(_console!, ct, since));
        StringAssert.Contains(entries, "boom from button");
        StringAssert.Contains(entries, "uncaught boom");
    }

    [TestMethod]
    public async Task ClickingALinkThatOpensATab_NamesTheTabInTheResult()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        var snapshot = await OpenBenchAsync();
        var clicked = await CoreTools.ClickAsync(RefFor(snapshot, "Open in new tab"), pages, ct);
        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        StringAssert.Contains(TextOf(clicked), $"New tab opened: [1] {_server!.OtherUrl}");
    }

    [TestMethod]
    public async Task SubmittingTheForm_ReportsNothingBecauseNothingChanged()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        var snapshot = await OpenBenchAsync();
        var clicked = await CoreTools.ClickAsync(RefFor(snapshot, "button \"Submit\""), pages, ct);

        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));
        Assert.AreEqual(1, TextOf(clicked).Split('\n').Length, $"a quiet action stays one line:\n{TextOf(clicked)}");
        StringAssert.StartsWith(TextOf(clicked), "Clicked ");
    }

    [TestMethod]
    public async Task Navigating_NamesThePageItLandedOn_AndCanBringTheSnapshotWithIt()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        var navigated = await CoreTools.NavigateAsync(_server!.IndexUrl, pages, ct, snapshot: true);

        Assert.IsFalse(navigated.IsError ?? false, TextOf(navigated));
        var lines = TextOf(navigated).Split('\n');
        Assert.AreEqual($"Navigated to {_server.IndexUrl} | MCP Bench", lines[0]);
        StringAssert.Contains(TextOf(navigated), "Snapshot:");
        StringAssert.Contains(TextOf(navigated), "button \"Submit\"");
    }

    [TestMethod]
    public async Task AClickThatNavigates_SaysThePageMovedAndTheRefsWithIt()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        var snapshot = await OpenBenchAsync();
        var clicked = await CoreTools.ClickAsync(RefFor(snapshot, "Other page"), pages, ct);

        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));
        StringAssert.Contains(TextOf(clicked), $"Page: {_server!.OtherUrl} | Other");
        StringAssert.Contains(TextOf(clicked), "Refs from the last snapshot no longer address this page");
    }

    private async Task<string> OpenBenchAsync()
    {
        var ct = CancellationToken.None;
        var navigated = await CoreTools.NavigateAsync(_server!.IndexUrl, _pages!, ct);
        Assert.IsFalse(navigated.IsError ?? false, TextOf(navigated));

        var snapshot = await CoreTools.SnapshotAsync(_pages!, ct, root_ref: null, max_depth: null);
        Assert.IsFalse(snapshot.IsError ?? false, TextOf(snapshot));
        return TextOf(snapshot);
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private static string RefFor(string snapshot, string needle)
    {
        foreach (var line in snapshot.Split('\n'))
        {
            if (!line.Contains(needle, StringComparison.Ordinal))
                continue;

            var marker = line.IndexOf("[ref=", StringComparison.Ordinal);
            if (marker < 0)
                continue;

            var start = marker + "[ref=".Length;
            return line[start..line.IndexOf(']', start)];
        }

        throw new AssertFailedException($"No ref found on a line containing '{needle}'.");
    }
}
