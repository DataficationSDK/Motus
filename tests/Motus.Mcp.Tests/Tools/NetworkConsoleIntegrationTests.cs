using System.Text;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the network and console tools through a real browser: mocking a request
/// with a JSON fixture and reading it back through the page, observing the request
/// log, capturing console output and an uncaught page error, and blocking a request.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class NetworkConsoleIntegrationTests
{
    private const string BlankPage = "data:text/html,<title>Net</title><p>Net</p>";

    private BrowserSessionManager? _sessions;
    private ConsoleService? _console;
    private NetworkService? _network;
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
        _console = new ConsoleService();
        _network = new NetworkService();
        _pages = new ActivePageService(_sessions, dialogService: null, _console, _network);
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
    public async Task MockLogConsoleAndError_OnARealBrowser()
    {
        var pages = _pages!;
        var network = _network!;
        var console = _console!;
        var ct = CancellationToken.None;

        // Mock an API with a JSON fixture (CORS header so the data: page can read it),
        // and block a second pattern. Both rules are registered on the active context.
        var headers = new Dictionary<string, string> { ["Access-Control-Allow-Origin"] = "*" };
        AssertOk(
            await NetworkTools.RouteFulfillAsync(
                "*api*", 200, "{\"value\":42}", "application/json", headers, pages, network, ct),
            "route_fulfill");
        AssertOk(await NetworkTools.RouteAbortAsync("*blocked*", "blockedbyclient", pages, network, ct), "route_abort");

        var routes = await NetworkTools.RouteListAsync(pages, network, ct);
        StringAssert.Contains(TextOf(routes), "*api*");
        StringAssert.Contains(TextOf(routes), "*blocked*");

        // Open the page in the routed context, then fetch the mocked API and log the value.
        AssertOk(await CoreTools.NavigateAsync(BlankPage, pages, ct), "navigate");
        AssertOk(
            await PageTools.EvaluateAsync(
                "fetch('https://example.test/api/x').then(function(r){return r.json();})"
                + ".then(function(d){console.log('got ' + d.value);}); 0",
                null, pages, ct),
            "fetch mocked api");

        var consoleText = await PollAsync(() => ConsoleTools.ConsoleMessages(console, ct), s => s.Contains("got 42"), ct);
        StringAssert.Contains(consoleText, "got 42");

        // Trigger a request that the abort rule blocks.
        AssertOk(
            await PageTools.EvaluateAsync("fetch('https://example.test/blocked/y').catch(function(){}); 0", null, pages, ct),
            "fetch blocked");

        var networkText = await PollAsync(
            () => NetworkTools.NetworkRequests(network, ct), s => s.Contains("FAILED"), ct);
        StringAssert.Contains(networkText, "/api/x");
        StringAssert.Contains(networkText, "200");
        StringAssert.Contains(networkText, "FAILED");

        // An uncaught error surfaces as a page error in the console buffer.
        AssertOk(
            await PageTools.EvaluateAsync("setTimeout(function(){ throw new Error('boom'); }, 10); 0", null, pages, ct),
            "schedule error");

        var errorText = await PollAsync(
            () => ConsoleTools.ConsoleMessages(console, ct), s => s.Contains(ConsoleService.PageErrorType), ct);
        StringAssert.Contains(errorText, ConsoleService.PageErrorType);
    }

    private static async Task<string> PollAsync(Func<CallToolResult> read, Func<string, bool> until, CancellationToken ct)
    {
        var accumulated = new StringBuilder();
        for (var attempt = 0; attempt < 30; attempt++)
        {
            accumulated.AppendLine(TextOf(read()));
            if (until(accumulated.ToString()))
                break;
            await Task.Delay(100, ct);
        }

        return accumulated.ToString();
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
