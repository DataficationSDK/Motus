using ModelContextProtocol.Protocol;
using Motus.Mcp;
using Motus.Mcp.Tests.Fixtures;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the case an agent hits on any page with a <c>target="_blank"</c> link: the click works,
/// the document loads, and until the session lists the tab the agent has no way to reach it.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class NewTabIntegrationTests
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
    public async Task ClickingALinkThatOpensATab_ListsTheNewTab()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        var navigated = await CoreTools.NavigateAsync(_server!.IndexUrl, service, ct);
        Assert.IsFalse(navigated.IsError ?? false, TextOf(navigated));

        var snapshot = await CoreTools.SnapshotAsync(
            pageService: service, cancellationToken: ct, root_ref: null, max_depth: null);

        var clicked = await CoreTools.ClickAsync(
            RefForLineContaining(TextOf(snapshot), "Open in new tab"), service, ct);
        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        var tabs = await WaitForTabsAsync(service, "other.html");

        var lines = tabs.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.AreEqual(2, lines.Length, $"Two tabs should be listed. Listing was:\n{tabs}");
        StringAssert.StartsWith(lines[0], "[0] " + _server.IndexUrl);
        StringAssert.StartsWith(lines[1], "[1] " + _server.OtherUrl);

        CollectionAssert.Contains(_server.Requests.ToArray(), "/other.html",
            "The browser never asked for the document the link points at.");
    }

    /// <summary>
    /// The tabs are numbered across every context the session holds, so the index the report prints
    /// for a tab opened in a second context is only right if the diff reads the same list the
    /// listing does.
    /// </summary>
    [TestMethod]
    public async Task ATabOpenedInASecondContext_IsListedAndReportedAcrossBothContexts()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        // The default context keeps the tab the session started with, so the new tab lands at an
        // index no listing of one context on its own would ever produce.
        await service.CreateContextAsync("work", ct);

        var navigated = await CoreTools.NavigateAsync(_server!.IndexUrl, service, ct);
        Assert.IsFalse(navigated.IsError ?? false, TextOf(navigated));

        var snapshot = await CoreTools.SnapshotAsync(
            pageService: service, cancellationToken: ct, root_ref: null, max_depth: null);

        var clicked = await CoreTools.ClickAsync(
            RefForLineContaining(TextOf(snapshot), "Open in new tab"), service, ct);
        Assert.IsFalse(clicked.IsError ?? false, TextOf(clicked));

        StringAssert.Contains(TextOf(clicked), "New tab opened: [2] " + _server.OtherUrl);

        var listing = await WaitForTabsAsync(service, "other.html");
        var lines = listing.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.AreEqual(3, lines.Length, $"Both contexts' tabs should be listed. Listing was:\n{listing}");
        StringAssert.Contains(lines[0], "context: default");
        StringAssert.Contains(lines[1], "context: work");
        StringAssert.StartsWith(lines[2], "[2] " + _server.OtherUrl);
        StringAssert.Contains(lines[2], "context: work");
    }

    /// <summary>
    /// The tab is listed as soon as the browser opens it, which is before the document it was
    /// opened for has arrived, so the URL is what is waited on rather than the count.
    /// </summary>
    private static async Task<string> WaitForTabsAsync(ActivePageService service, string urlSuffix)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        var listing = string.Empty;

        while (DateTime.UtcNow < deadline)
        {
            listing = TextOf(await SessionTools.TabListAsync(service, CancellationToken.None));
            if (listing.Contains(urlSuffix, StringComparison.Ordinal))
                return listing;

            await Task.Delay(100);
        }

        Assert.Fail($"No tab showing '{urlSuffix}' was listed. Listing was:\n{listing}");
        return listing;
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

    private static string RefForLineContaining(string snapshot, string needle)
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
