using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the tab, context, history, dialog, and evaluate tools through a real
/// browser: evaluating an expression, opening and listing tabs, switching tab,
/// answering a dialog, moving through history, and creating, selecting, and closing
/// a context.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PageSessionIntegrationTests
{
    private const string PageA =
        "data:text/html,<title>A</title><script>window.answer=42;</script><p>Page A</p>";
    private const string PageB = "data:text/html,<title>B</title><p>Page B</p>";

    private BrowserSessionManager? _sessions;
    private DialogService? _dialogs;
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
        _dialogs = new DialogService();
        _pages = new ActivePageService(_sessions, _dialogs);
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
    public async Task TabsDialogEvaluateAndHistory_OnARealBrowser()
    {
        var service = _pages!;
        var dialogs = _dialogs!;
        var ct = CancellationToken.None;

        Assert.IsFalse((await CoreTools.NavigateAsync(PageA, service, ct)).IsError ?? false, "navigate");

        // evaluate, page-level: a value defined on the page comes back as structured JSON.
        var eval = await PageTools.EvaluateAsync("window.answer", null, service, ct);
        Assert.IsFalse(eval.IsError ?? false, TextOf(eval));
        Assert.IsNotNull(eval.StructuredContent);
        Assert.AreEqual(42, eval.StructuredContent.Value.GetInt32());

        // Open and navigate a second tab, then confirm both are listed.
        AssertOk(await SessionTools.TabOpenAsync(PageB, service, ct), "tab_open");
        var list = await SessionTools.TabListAsync(service, ct);
        StringAssert.Contains(TextOf(list), "[0]");
        StringAssert.Contains(TextOf(list), "[1]");

        // Switch back to the first tab.
        AssertOk(await SessionTools.TabSelectAsync(0, service, ct), "tab_select");

        // Schedule a dialog so it fires after this call returns (rather than blocking a
        // click), then answer it once the handler has captured it.
        AssertOk(
            await PageTools.EvaluateAsync("setTimeout(function(){ window.alert('hello'); }, 50); 0", null, service, ct),
            "schedule dialog");

        CallToolResult dialog = ToolResultHelper.Error("dialog not seen");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            dialog = await PageTools.HandleDialogAsync(true, null, dialogs, ct);
            if (!(dialog.IsError ?? false))
                break;
            await Task.Delay(100, ct);
        }

        AssertOk(dialog, "handle_dialog");

        // History: the first tab has a single entry, so back reports no previous entry; reload succeeds.
        AssertOk(await PageTools.GoBackAsync(service, ct), "go_back");
        AssertOk(await PageTools.GoForwardAsync(service, ct), "go_forward");
        AssertOk(await PageTools.ReloadAsync(service, ct), "reload");

        // Contexts: create, switch back to default, and close.
        AssertOk(await SessionTools.ContextCreateAsync("userB", service, ct), "context_create");
        AssertOk(SessionTools.ContextSelect(BrowserSessionManager.DefaultContextName, service, ct), "context_select");
        AssertOk(await SessionTools.ContextCloseAsync("userB", service, ct), "context_close");
    }

    private static void AssertOk(CallToolResult result, string label)
        => Assert.IsFalse(result.IsError ?? false, $"{label} should succeed: {TextOf(result)}");

    private static string TextOf(CallToolResult result)
        => result.Content[0] is TextContentBlock t ? t.Text : string.Empty;

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
