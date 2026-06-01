using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the performance tool through a real browser: navigating to a page and
/// confirming metrics come back, collected after the navigation by the telemetry
/// the session manager enables at launch.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class PerformanceIntegrationTests
{
    private const string SamplePage =
        "data:text/html,<title>Perf</title><main><h1>Hello</h1><p>One</p><p>Two</p><button>Go</button></main>";

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
    public async Task GetPerformance_AfterNavigation_OnARealBrowser()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate");

        var result = await PerformanceTools.GetPerformanceAsync(pages, ct);
        AssertOk(result, "get_performance");

        Assert.IsNotNull(result.StructuredContent, "expected structured metrics");
        var content = result.StructuredContent!.Value;

        // The collection ran, so the timestamp is set.
        var collectedAt = content.GetProperty("collectedAtUtc").GetString();
        Assert.IsFalse(string.IsNullOrEmpty(collectedAt), "collectedAtUtc should be populated");

        // Performance.getMetrics reports the DOM node count for any rendered page, so it
        // is the reliable signal that real metrics came back rather than empty defaults.
        Assert.AreEqual(JsonValueKind.Number, content.GetProperty("domNodeCount").ValueKind,
            "domNodeCount should be measured after navigation");
        Assert.IsTrue(content.GetProperty("domNodeCount").GetInt32() > 0, "expected a non-zero DOM node count");
    }

    private static void AssertOk(CallToolResult result, string label)
        => Assert.IsFalse(result.IsError ?? false, $"{label} should succeed: {TextOf(result)}");

    private static string TextOf(CallToolResult result)
        => result.Content.Count > 0 && result.Content[0] is TextContentBlock t ? t.Text : string.Empty;

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
