using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the recording and codegen tools through a real browser: generating a page
/// object model for a live page, and writing a trace ZIP and a HAR file to disk.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class ArtifactIntegrationTests
{
    private const string SamplePage =
        "data:text/html,<title>Sample</title><main><h1>Hello</h1>"
        + "<input id='name' placeholder='Name'><button id='go'>Go</button></main>";

    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;
    private readonly List<string> _artifacts = [];

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

        foreach (var path in _artifacts)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public async Task GeneratePom_ForALivePage_ReturnsSourceForTheNamedClass()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate");

        var result = await CodegenTools.GeneratePomAsync("Sample.Generated", "SamplePageModel", pages, ct);
        AssertOk(result, "generate_pom");

        var source = TextOf(result);
        StringAssert.Contains(source, "namespace Sample.Generated");
        StringAssert.Contains(source, "class SamplePageModel");
    }

    [TestMethod]
    public async Task TraceStartStop_WritesANonEmptyZip()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;
        var path = Path.Combine(Path.GetTempPath(), $"motus-trace-test-{Guid.NewGuid():N}.zip");
        _artifacts.Add(path);

        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate");
        AssertOk(await RecordingTools.TraceStartAsync(screenshots: true, snapshots: true, pages, ct), "trace_start");
        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate-again");
        AssertOk(await RecordingTools.TraceStopAsync(path, pages, ct), "trace_stop");

        Assert.IsTrue(File.Exists(path), "trace file should exist");
        Assert.IsTrue(new FileInfo(path).Length > 0, "trace file should be non-empty");
    }

    [TestMethod]
    public async Task HarStartStop_WritesAParsableArchive()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;
        var path = Path.Combine(Path.GetTempPath(), $"motus-har-test-{Guid.NewGuid():N}.har");
        _artifacts.Add(path);

        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate");
        AssertOk(await RecordingTools.HarStartAsync(pages, ct), "har_start");
        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate-again");
        AssertOk(await RecordingTools.HarStopAsync(path, pages, ct), "har_stop");

        Assert.IsTrue(File.Exists(path), "HAR file should exist");

        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path, ct));
        var log = doc.RootElement.GetProperty("log");
        Assert.AreEqual(JsonValueKind.Array, log.GetProperty("entries").ValueKind,
            "the HAR log should carry an entries array");
    }

    [TestMethod]
    public async Task VideoStartStop_WritesAPlayableAviAtViewportResolution()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;
        var path = Path.Combine(Path.GetTempPath(), $"motus-video-test-{Guid.NewGuid():N}.avi");
        _artifacts.Add(path);

        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate");
        AssertOk(await CoordinateTools.ResizeAsync(640, 480, pages, ct), "resize");
        AssertOk(await RecordingTools.VideoStartAsync(path, pages, ct), "video_start");

        // Produce on-screen changes so the screencast emits frames.
        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate-again");
        AssertOk(await InteractionTools.WaitForAsync(500, null, null, pages, ct), "wait");

        var stop = await RecordingTools.VideoStopAsync(pages, ct);
        AssertOk(stop, "video_stop");
        StringAssert.Contains(TextOf(stop), path);

        Assert.IsTrue(File.Exists(path), "video file should exist");

        // RIFF....AVI header and the capture dimensions in the main header
        // (avih frame width/height at offsets 0x40 and 0x44).
        var bytes = await File.ReadAllBytesAsync(path, ct);
        Assert.IsTrue(bytes.Length > 0x48, "video file should be non-trivial");
        Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.AreEqual("AVI ", System.Text.Encoding.ASCII.GetString(bytes, 8, 4));
        Assert.AreEqual(640, BitConverter.ToInt32(bytes, 0x40), "frame width should match the viewport");
        Assert.AreEqual(480, BitConverter.ToInt32(bytes, 0x44), "frame height should match the viewport");
    }

    [TestMethod]
    public async Task VideoStop_WithoutAStart_ReturnsError()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        AssertOk(await CoreTools.NavigateAsync(SamplePage, pages, ct), "navigate");

        var result = await RecordingTools.VideoStopAsync(pages, ct);
        Assert.IsTrue(result.IsError ?? false, "video_stop without a recording should be an error");
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
