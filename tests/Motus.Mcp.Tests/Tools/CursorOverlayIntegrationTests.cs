using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives a real browser with the pseudo-cursor overlay enabled and asserts that the overlay is
/// injected, becomes visible after a move, and reflects the CSS cursor of the element underneath.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class CursorOverlayIntegrationTests
{
    // Two fixed-position targets with distinct cursor styles so we can move to known coordinates.
    // No '#' anywhere: a data: URL treats it as a fragment and would truncate the document.
    private const string SamplePage =
        "data:text/html,<title>Cursor</title><body style='margin:0'>"
        + "<a id='link' style='position:fixed;left:40px;top:40px;cursor:pointer'>link</a>"
        + "<div id='plain' style='position:fixed;left:200px;top:200px;cursor:default'>plain</div></body>";

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
            ShowCursor = true,
        });
        _pages = new ActivePageService(_sessions);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_pages is not null)
            _pages.Shutdown();
        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    [TestMethod]
    public async Task Overlay_IsInjected_BecomesVisible_AndReflectsElementCursor()
    {
        var ct = CancellationToken.None;
        var page = await _pages!.GetOrCreateActivePageAsync(ct);
        await page.GotoAsync(SamplePage);

        // The overlay element is present from the init script even before any movement.
        var present = await page.EvaluateAsync<bool>("!!document.querySelector('[data-motus-cursor]')");
        Assert.IsTrue(present, "the cursor overlay should be injected into the page");

        // Over the pointer link, the reflected cursor key should resolve to 'pointer'.
        await page.Mouse.MoveAsync(45, 48);
        var pointerKey = await ReadCursorKeyAsync(page, ct);
        Assert.AreEqual("pointer", pointerKey, "the cursor should reflect the link's pointer style");

        // The overlay becomes visible once it has a position to track.
        var opacity = await page.EvaluateAsync<string>(
            "document.querySelector('[data-motus-cursor]').style.opacity");
        Assert.AreEqual("1", opacity, "the cursor should be visible after a move");

        // Over a default-cursor element the key should switch away from 'pointer'.
        await page.Mouse.MoveAsync(210, 208);
        var defaultKey = await ReadCursorKeyAsync(page, ct);
        Assert.AreNotEqual("pointer", defaultKey, "the cursor should stop reflecting pointer off the link");
    }

    // Reflection is requestAnimationFrame-throttled, so poll briefly for the attribute to settle.
    private static async Task<string?> ReadCursorKeyAsync(IPage page, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var key = await page.EvaluateAsync<string?>(
                "document.querySelector('[data-motus-cursor]')?.getAttribute('data-cursor-key')");
            if (!string.IsNullOrEmpty(key))
                return key;
            await Task.Delay(25, ct);
        }

        return null;
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
