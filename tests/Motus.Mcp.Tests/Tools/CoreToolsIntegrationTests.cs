using System.Linq;
using ModelContextProtocol.Protocol;
using Motus;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the core tools through a real browser: navigate, read the page with a
/// snapshot, act on the refs it assigns, and capture a screenshot.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class CoreToolsIntegrationTests
{
    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;

    [TestInitialize]
    public void Setup()
    {
        var executablePath = ResolveInstalledBrowser();
        if (executablePath is null)
            Assert.Inconclusive("No installed browser found; skipping integration test.");

        _sessions = new BrowserSessionManager(new McpServerLaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
        });
        _pages = new ActivePageService(_sessions);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_pages is not null)
            await _pages.DisposeAsync();
        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    [TestMethod]
    public async Task Navigate_Snapshot_Click_Type_Screenshot()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        var nav = await CoreTools.NavigateAsync(
            "data:text/html,<button>Go</button><input aria-label=\"Name\"/>", service, ct);
        Assert.IsFalse(nav.IsError ?? false, "navigate should succeed.");

        var snap = await CoreTools.SnapshotAsync(null, null, service, ct);
        var snapText = ((TextContentBlock)snap.Content[0]).Text;
        StringAssert.Contains(snapText, "[ref=e");

        var click = await CoreTools.ClickAsync(RefForLineContaining(snapText, "Go"), null, service, ct);
        Assert.IsFalse(click.IsError ?? false, "click should succeed.");

        var type = await CoreTools.TypeAsync(RefForLineContaining(snapText, "Name"), "hello", null, null, service, ct);
        Assert.IsFalse(type.IsError ?? false, "type should succeed.");

        var shot = await CoreTools.ScreenshotAsync(null, service, ct);
        Assert.IsFalse(shot.IsError ?? false, "screenshot should succeed.");
        Assert.IsInstanceOfType<ImageContentBlock>(shot.Content[0]);
    }

    [TestMethod]
    public async Task Snapshot_MaxDepth_TruncatesTheTree()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        await CoreTools.NavigateAsync(
            "data:text/html,<main><ul><li>one</li><li>two</li></ul></main>", service, ct);

        var full = ((TextContentBlock)(await CoreTools.SnapshotAsync(null, null, service, ct)).Content[0]).Text;
        var shallow = ((TextContentBlock)(await CoreTools.SnapshotAsync(null, 0, service, ct)).Content[0]).Text;

        Assert.IsTrue(
            LineCount(shallow) < LineCount(full),
            $"max_depth=0 should render fewer lines (shallow={LineCount(shallow)}, full={LineCount(full)}).");
    }

    private static int LineCount(string text)
        => text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

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
            var end = line.IndexOf(']', start);
            return line[start..end];
        }

        throw new AssertFailedException($"No ref found on a line containing '{needle}'.");
    }

    private static string? ResolveInstalledBrowser()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".motus",
            "browsers");

        foreach (var marker in new[] { ".installed.chromium", ".installed" })
        {
            var markerPath = Path.Combine(cacheDir, marker);
            if (!File.Exists(markerPath))
                continue;

            var executablePath = File.ReadAllText(markerPath).Trim();
            if (File.Exists(executablePath))
                return executablePath;
        }

        return null;
    }
}
