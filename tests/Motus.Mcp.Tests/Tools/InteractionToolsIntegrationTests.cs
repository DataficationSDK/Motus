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
            await _pages.DisposeAsync();
        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    [TestMethod]
    public async Task FullInteractionSequence_OnARealForm()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        Assert.IsFalse((await CoreTools.NavigateAsync(Form, service, ct)).IsError ?? false, "navigate");

        var snapText = TextOf(await CoreTools.SnapshotAsync(null, null, service, ct));
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

        var path = Path.Combine(Path.GetTempPath(), $"motus_upload_{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "hello", ct);
        try
        {
            var doc = RefForLineContaining(snapText, "Doc");
            AssertOk(await InteractionTools.UploadFilesAsync(doc, [path], service, ct), "upload_files");
        }
        finally
        {
            File.Delete(path);
        }

        AssertOk(await InteractionTools.WaitForElementAsync(accept, "visible", service, ct), "wait_for_element");
        AssertOk(await InteractionTools.WaitForAsync(null, "Ready", null, service, ct), "wait_for text");
        AssertOk(await InteractionTools.PressKeyAsync("Escape", service, ct), "press_key");
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
