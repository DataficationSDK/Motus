using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class RecordingToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AccessibilitySnapshot EmptySnapshot() => new([], IgnoredCount: 0, DiagnosticMessage: null);

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public async Task TraceStart_StartsTracingWithGivenOptions()
    {
        var service = new FakeNetworkPageService();

        var result = await RecordingTools.TraceStartAsync(screenshots: true, snapshots: false, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Context.TracingFake.Started);
        Assert.AreEqual(true, service.Context.TracingFake.StartedWith?.Screenshots);
        Assert.AreEqual(false, service.Context.TracingFake.StartedWith?.Snapshots);
    }

    [TestMethod]
    public async Task TraceStart_DefaultsOptionsToTrue()
    {
        var service = new FakeNetworkPageService();

        await RecordingTools.TraceStartAsync(screenshots: null, snapshots: null, service, Ct);

        Assert.AreEqual(true, service.Context.TracingFake.StartedWith?.Screenshots);
        Assert.AreEqual(true, service.Context.TracingFake.StartedWith?.Snapshots);
    }

    [TestMethod]
    public async Task TraceStop_WritesToProvidedPath()
    {
        var service = new FakeNetworkPageService();

        var result = await RecordingTools.TraceStopAsync("/tmp/example-trace.zip", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsTrue(service.Context.TracingFake.Stopped);
        Assert.AreEqual("/tmp/example-trace.zip", service.Context.TracingFake.StoppedWith?.Path);
        StringAssert.Contains(TextOf(result), "/tmp/example-trace.zip");
    }

    [TestMethod]
    public async Task TraceStop_GeneratesPathWhenOmitted()
    {
        var service = new FakeNetworkPageService();

        var result = await RecordingTools.TraceStopAsync(path: null, service, Ct);

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

        var result = await RecordingTools.HarStopAsync("/tmp/example.har", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual("/tmp/example.har", page.HarStoppedPath);
        StringAssert.Contains(TextOf(result), "/tmp/example.har");
    }

    [TestMethod]
    public async Task HarStop_GeneratesPathWhenOmitted()
    {
        var page = new FakeToolPage(EmptySnapshot());
        var service = new FakeNetworkPageService(page);

        await RecordingTools.HarStopAsync(path: null, service, Ct);

        Assert.IsFalse(string.IsNullOrEmpty(page.HarStoppedPath));
        StringAssert.EndsWith(page.HarStoppedPath, ".har");
    }

    [TestMethod]
    public async Task HarStop_WhenWriteThrows_ReturnsError()
    {
        var page = new FakeToolPage(EmptySnapshot()) { HarError = new IOException("disk full") };
        var service = new FakeNetworkPageService(page);

        var result = await RecordingTools.HarStopAsync("/tmp/example.har", service, Ct);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "disk full");
    }
}
