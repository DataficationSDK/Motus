using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class RecordingToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private string _outputDir = string.Empty;
    private SecurityPolicy _policy = null!;

    [TestInitialize]
    public void Setup()
    {
        // A directory per test: the net8.0 and net10.0 test assemblies run as separate processes
        // and would otherwise contend for one fixed path under the temp dir.
        _outputDir = Path.Combine(Path.GetTempPath(), $"motus_recording_{Guid.NewGuid():N}");
        _policy = new SecurityPolicy(new McpServerLaunchOptions { OutputDirectory = _outputDir });
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            Directory.Delete(_outputDir, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Nothing was written, which several of these tests never get as far as doing.
        }
    }

    private static AccessibilitySnapshot EmptySnapshot() => new([], IgnoredCount: 0, DiagnosticMessage: null);

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public async Task TraceStart_StartsTracingWithGivenOptions()
    {
        var service = new FakeNetworkPageService();

        var result = await RecordingTools.TraceStartAsync(
            pageService: service,
            cancellationToken: Ct,
            screenshots: true,
            snapshots: false);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Context.TracingFake.Started);
        Assert.AreEqual(true, service.Context.TracingFake.StartedWith?.Screenshots);
        Assert.AreEqual(false, service.Context.TracingFake.StartedWith?.Snapshots);
    }

    [TestMethod]
    public async Task TraceStart_DefaultsOptionsToTrue()
    {
        var service = new FakeNetworkPageService();

        await RecordingTools.TraceStartAsync(
            pageService: service,
            cancellationToken: Ct,
            screenshots: null,
            snapshots: null);

        Assert.AreEqual(true, service.Context.TracingFake.StartedWith?.Screenshots);
        Assert.AreEqual(true, service.Context.TracingFake.StartedWith?.Snapshots);
    }

    [TestMethod]
    public async Task TraceStop_WritesToProvidedPath()
    {
        var service = new FakeNetworkPageService();

        var expected = Path.Combine(_outputDir, "example-trace.zip");

        var result = await RecordingTools.TraceStopAsync(
            pageService: service,
            cancellationToken: Ct,
            path: "example-trace.zip",
            policy: _policy);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.IsTrue(service.Context.TracingFake.Stopped);
        Assert.AreEqual(expected, service.Context.TracingFake.StoppedWith?.Path);
        StringAssert.Contains(TextOf(result), expected);
    }

    [TestMethod]
    public async Task TraceStop_GeneratesPathWhenOmitted()
    {
        var service = new FakeNetworkPageService();

        var result = await RecordingTools.TraceStopAsync(
            pageService: service,
            cancellationToken: Ct,
            path: null,
            policy: _policy);

        var path = service.Context.TracingFake.StoppedWith?.Path;
        Assert.IsFalse(string.IsNullOrEmpty(path));
        StringAssert.Contains(path, "motus-trace");
        StringAssert.EndsWith(path, ".zip");
        StringAssert.Contains(TextOf(result), path);
    }

    [TestMethod]
    public async Task HarStart_BeginsRecordingOnTheActivePage()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.HarStartAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(page.HarRecording);
    }

    [TestMethod]
    public async Task HarStop_WritesToProvidedPath()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var expected = Path.Combine(_outputDir, "example.har");

        var result = await RecordingTools.HarStopAsync(
            pageService: service,
            cancellationToken: Ct,
            path: "example.har",
            policy: _policy);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual(expected, page.HarStoppedPath);
        StringAssert.Contains(TextOf(result), expected);
    }

    [TestMethod]
    public async Task HarStop_GeneratesPathWhenOmitted()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        await RecordingTools.HarStopAsync(
            pageService: service, cancellationToken: Ct, path: null, policy: _policy);

        Assert.IsFalse(string.IsNullOrEmpty(page.HarStoppedPath));
        StringAssert.EndsWith(page.HarStoppedPath, ".har");
    }

    [TestMethod]
    public async Task HarStop_WhenWriteThrows_ReturnsError()
    {
        var page = new FakeToolPage(EmptySnapshot()) { HarError = new IOException("disk full") };
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.HarStopAsync(
            pageService: service,
            cancellationToken: Ct,
            path: "example.har",
            policy: _policy);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "disk full");
    }

    [TestMethod]
    public async Task VideoStart_BeginsRecordingOnTheActivePage()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var expected = Path.Combine(_outputDir, "example.avi");

        var result = await RecordingTools.VideoStartAsync(
            pageService: service,
            cancellationToken: Ct,
            path: "example.avi",
            policy: _policy);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual(expected, page.VideoRecordingPath);
        StringAssert.Contains(TextOf(result), expected);
    }

    [TestMethod]
    public async Task VideoStart_GeneratesPathWhenOmitted()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.VideoStartAsync(
            pageService: service,
            cancellationToken: Ct,
            path: null,
            policy: _policy);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsFalse(string.IsNullOrEmpty(page.VideoRecordingPath));
        StringAssert.Contains(page.VideoRecordingPath, "motus-video");
        StringAssert.EndsWith(page.VideoRecordingPath, ".avi");
    }

    [TestMethod]
    public async Task VideoStart_WhenAlreadyRecording_ReturnsError()
    {
        var page = new FakeToolPage(EmptySnapshot())
        {
            VideoStartError = new InvalidOperationException("Video recording is already in progress on this page."),
        };
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.VideoStartAsync(
            pageService: service,
            cancellationToken: Ct,
            path: null,
            policy: _policy);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "already in progress");
    }

    [TestMethod]
    public async Task VideoStop_ReturnsTheFinalizedPath()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);
        var expected = Path.Combine(_outputDir, "example.avi");
        await RecordingTools.VideoStartAsync(
            pageService: service,
            cancellationToken: Ct,
            path: "example.avi",
            policy: _policy);

        var result = await RecordingTools.VideoStopAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(expected, page.VideoStoppedPath);
        StringAssert.Contains(TextOf(result), expected);
    }

    [TestMethod]
    public async Task VideoStop_WithoutARecording_ReturnsError()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.VideoStopAsync(service, Ct);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No video recording");
    }

    // --- where artifacts may be written ---

    [TestMethod]
    [DataRow("/etc/motus-escape.zip", DisplayName = "an absolute path")]
    [DataRow("../motus-escape.zip", DisplayName = "a path that climbs out")]
    public async Task TraceStop_PathOutsideTheOutputDirectory_IsRefused(string path)
    {
        var service = new FakeNetworkPageService();

        var result = await RecordingTools.TraceStopAsync(
            pageService: service, cancellationToken: Ct, path: path, policy: _policy);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "must stay inside the output directory");
        Assert.IsFalse(service.Context.TracingFake.Stopped, "the trace should not have been written at all");
    }

    [TestMethod]
    [DataRow("/etc/motus-escape.har", DisplayName = "an absolute path")]
    [DataRow("../motus-escape.har", DisplayName = "a path that climbs out")]
    public async Task HarStop_PathOutsideTheOutputDirectory_IsRefused(string path)
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.HarStopAsync(
            pageService: service, cancellationToken: Ct, path: path, policy: _policy);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "must stay inside the output directory");
        Assert.IsNull(page.HarStoppedPath);
    }

    [TestMethod]
    [DataRow("/etc/motus-escape.avi", DisplayName = "an absolute path")]
    [DataRow("../motus-escape.avi", DisplayName = "a path that climbs out")]
    public async Task VideoStart_PathOutsideTheOutputDirectory_IsRefused(string path)
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.VideoStartAsync(
            pageService: service, cancellationToken: Ct, path: path, policy: _policy);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "must stay inside the output directory");
        Assert.IsNull(page.VideoRecordingPath);
    }

    [TestMethod]
    public async Task TraceStop_WithUnrestrictedFileAccess_WritesWhereItWasAsked()
    {
        var service = new FakeNetworkPageService();
        var unrestricted = new SecurityPolicy(new McpServerLaunchOptions
        {
            OutputDirectory = _outputDir,
            AllowUnrestrictedFileAccess = true,
        });
        var elsewhere = Path.Combine(Path.GetTempPath(), $"motus_unrestricted_{Guid.NewGuid():N}.zip");

        var result = await RecordingTools.TraceStopAsync(
            pageService: service, cancellationToken: Ct, path: elsewhere, policy: unrestricted);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        Assert.AreEqual(elsewhere, service.Context.TracingFake.StoppedWith?.Path);
    }
}
