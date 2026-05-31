using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class PerformanceToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static AccessibilitySnapshot EmptySnapshot() => new([], IgnoredCount: 0, DiagnosticMessage: null);

    private static PerformanceMetrics SampleMetrics(string? diagnostic = null) => new(
        Lcp: 1800,
        Fcp: 1200,
        Ttfb: 85.5,
        Cls: 0.05,
        Inp: 120,
        JsHeapSize: 42_000_000,
        DomNodeCount: 1200,
        LayoutShifts: [new LayoutShiftEntry(0.05, ["div#hero"])],
        CollectedAtUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        DiagnosticMessage: diagnostic);

    private static JsonElement Structured(CallToolResult result)
    {
        Assert.IsNotNull(result.StructuredContent, "expected structured content");
        return result.StructuredContent!.Value;
    }

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public async Task GetPerformance_WithMetrics_ReturnsStructuredVitals()
    {
        var page = new FakeToolPage(EmptySnapshot()) { PerformanceMetrics = SampleMetrics() };
        var service = new FakeActivePageService(page);

        var result = await PerformanceTools.GetPerformanceAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        var content = Structured(result);
        Assert.AreEqual(1800, content.GetProperty("lcp").GetDouble());
        Assert.AreEqual(1200, content.GetProperty("fcp").GetDouble());
        Assert.AreEqual(85.5, content.GetProperty("ttfb").GetDouble());
        Assert.AreEqual(0.05, content.GetProperty("cls").GetDouble());
        Assert.AreEqual(120, content.GetProperty("inp").GetDouble());
        Assert.AreEqual(42_000_000, content.GetProperty("jsHeapSize").GetInt64());
        Assert.AreEqual(1200, content.GetProperty("domNodeCount").GetInt32());
        Assert.AreEqual(1, content.GetProperty("layoutShiftCount").GetInt32());
        StringAssert.Contains(content.GetProperty("collectedAtUtc").GetString(), "2024-01-01");
    }

    [TestMethod]
    public async Task GetPerformance_WithNullVitals_EmitsJsonNulls()
    {
        // Before any vitals settle, the scalar metrics can be null; they should serialize
        // as JSON null rather than being dropped, so the agent sees they are unmeasured.
        var page = new FakeToolPage(EmptySnapshot())
        {
            PerformanceMetrics = new PerformanceMetrics(
                Lcp: null, Fcp: null, Ttfb: null, Cls: null, Inp: null,
                JsHeapSize: null, DomNodeCount: null, LayoutShifts: [],
                CollectedAtUtc: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
        };
        var service = new FakeActivePageService(page);

        var result = await PerformanceTools.GetPerformanceAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        var content = Structured(result);
        Assert.AreEqual(JsonValueKind.Null, content.GetProperty("lcp").ValueKind);
        Assert.AreEqual(0, content.GetProperty("layoutShiftCount").GetInt32());
    }

    [TestMethod]
    public async Task GetPerformance_SurfacesDiagnosticMessage()
    {
        var page = new FakeToolPage(EmptySnapshot()) { PerformanceMetrics = SampleMetrics("Partial data.") };
        var service = new FakeActivePageService(page);

        var result = await PerformanceTools.GetPerformanceAsync(service, Ct);

        Assert.AreEqual("Partial data.", Structured(result).GetProperty("diagnosticMessage").GetString());
        StringAssert.Contains(TextOf(result), "Partial data.");
    }

    [TestMethod]
    public async Task GetPerformance_NoMetrics_ReportsNoneWithoutError()
    {
        var page = new FakeToolPage(EmptySnapshot()) { PerformanceMetrics = null };
        var service = new FakeActivePageService(page);

        var result = await PerformanceTools.GetPerformanceAsync(service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsNull(result.StructuredContent);
        StringAssert.Contains(TextOf(result), "No performance metrics");
    }

    [TestMethod]
    public async Task GetPerformance_WhenCollectionThrows_ReturnsError()
    {
        var page = new FakeToolPage(EmptySnapshot()) { PerformanceError = new InvalidOperationException("boom") };
        var service = new FakeActivePageService(page);

        var result = await PerformanceTools.GetPerformanceAsync(service, Ct);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "boom");
    }
}
