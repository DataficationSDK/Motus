using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the interaction tools through a real browser against a small form:
/// toggling a checkbox, selecting an option, focusing/clearing/pressing on a text
/// field, uploading a file, and waiting on element and page conditions.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class InteractionToolsIntegrationTests
{
    private const string Form =
        "data:text/html,<form>"
        + "<input type=\"checkbox\" aria-label=\"Accept\"/>"
        + "<select aria-label=\"Country\"><option value=\"us\">US</option><option value=\"ca\">CA</option></select>"
        + "<input aria-label=\"Name\" value=\"old\"/>"
        + "<input type=\"file\" aria-label=\"Doc\"/>"
        + "<p>Ready</p></form>";

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
            _pages.Shutdown();
        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    [TestMethod]
    public async Task FullInteractionSequence_OnARealForm()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        Assert.IsFalse((await CoreTools.NavigateAsync(Form, service, ct)).IsError ?? false, "navigate");

        var snapText = TextOf(await CoreTools.SnapshotAsync(
            pageService: service,
            cancellationToken: ct,
            root_ref: null,
            max_depth: null));
        StringAssert.Contains(snapText, "[ref=e");

        var accept = RefForLineContaining(snapText, "Accept");
        var country = RefForLineContaining(snapText, "Country");
        var name = RefForLineContaining(snapText, "Name");

        AssertOk(await InteractionTools.SetCheckedAsync(accept, true, service, ct), "set_checked");
        AssertOk(await InteractionTools.SelectOptionAsync(country, ["ca"], service, ct), "select_option");
        AssertOk(await InteractionTools.HoverAsync(accept, service, ct), "hover");
        AssertOk(await InteractionTools.FocusAsync(name, service, ct), "focus");
        AssertOk(await InteractionTools.ClearAsync(name, service, ct), "clear");
        AssertOk(await InteractionTools.PressAsync(name, "Tab", service, ct), "press");

        // Uploads read only from the directories the session is allowed to read, standing in here
        // for the roots an MCP client reports.
        var uploadDir = Path.Combine(Path.GetTempPath(), $"motus_upload_{Guid.NewGuid():N}");
        Directory.CreateDirectory(uploadDir);
        var path = Path.Combine(uploadDir, "upload.txt");
        await File.WriteAllTextAsync(path, "hello", ct);
        var policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = uploadDir })
        {
            ReadRootsOverride = _ => new ValueTask<IReadOnlyList<string>>(new[] { uploadDir }),
        };
        try
        {
            var doc = RefForLineContaining(snapText, "Doc");
            AssertOk(
                await InteractionTools.UploadFilesAsync(doc, [path], service, ct, server: null, policy: policy),
                "upload_files");
        }
        finally
        {
            Directory.Delete(uploadDir, recursive: true);
        }

        AssertOk(await InteractionTools.WaitForElementAsync(accept, "visible", service, ct), "wait_for_element");
        AssertOk(await InteractionTools.WaitForAsync(
            pageService: service,
            cancellationToken: ct,
            time: null,
            text: "Ready",
            text_gone: null), "wait_for text");
        AssertOk(await InteractionTools.PressKeyAsync("Escape", service, ct), "press_key");
    }

    /// <summary>
    /// The tools act through the same locator path as the library, so an element below the fold is
    /// scrolled to rather than timed out on. The drag starts at the top of the page and drops far
    /// below it, so the drop target is only reachable once the pointer is already down.
    /// </summary>
    [TestMethod]
    public async Task ClickSetCheckedAndDrag_ReachElementsBelowTheFold()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        const string Page =
            "data:text/html,<div id=src onmousedown=\"window.down='src'\" style='width:80px;height:40px'>drag</div>"
            + "<div style='height:3000px'></div>"
            + "<button id=b onclick=\"this.textContent='clicked'\">Go</button>"
            + "<input type=checkbox id=c>"
            + "<div id=dst onmouseup=\"window.up='dst'\" style='width:80px;height:40px'>drop</div>";

        AssertOk(await CoreTools.NavigateAsync(Page, service, ct), "navigate");

        AssertOk(await CoreTools.ClickAsync("#b", service, ct), "click");
        AssertOk(await InteractionTools.SetCheckedAsync("#c", true, service, ct), "set_checked");
        AssertOk(await CoordinateTools.DragAsync(
            pageService: service,
            cancellationToken: ct,
            start_ref: "#src",
            end_ref: "#dst"), "drag");

        var page = await service.GetOrCreateActivePageAsync(ct);
        Assert.AreEqual("clicked", await page.EvaluateAsync<string>("document.getElementById('b').textContent"));
        Assert.IsTrue(await page.EvaluateAsync<bool>("document.getElementById('c').checked"));
        Assert.AreEqual("src", await page.EvaluateAsync<string>("window.down || ''"));
        Assert.AreEqual("dst", await page.EvaluateAsync<string>("window.up || ''"));
    }

    private static void AssertOk(CallToolResult result, string label)
        => Assert.IsFalse(result.IsError ?? false, $"{label} should succeed: {TextOf(result)}");

    private static string TextOf(CallToolResult result)
        => result.Content[0] is TextContentBlock t ? t.Text : string.Empty;

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
