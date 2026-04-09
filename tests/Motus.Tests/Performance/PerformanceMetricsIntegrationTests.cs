using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Performance;

[TestClass]
public class PerformanceMetricsIntegrationTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(_transport, _registry, process: null, tempUserDataDir: null,
                                     handleSigint: false, handleSigterm: false);
        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task OnPageCreated_SendsPerformanceEnableAndScriptInjection()
    {
        var options = new PerformanceOptions { Enable = true };
        var hook = new PerformanceMetricsCollector(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        // Queue responses for page creation (6 responses) + Performance.enable + addScriptToEvaluateOnNewDocument
        QueuePageOnContextResponses("target-1", "session-1", 3);
        // Performance.enable response
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        // Page.addScriptToEvaluateOnNewDocument response
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"identifier": "perf-1"}}""");

        var page = (Motus.Page)await context.NewPageAsync();

        // Verify the Performance.enable and script injection were sent
        // The page creation sends 6 commands (ids 3-8), then our hook sends 2 more (9-10)
        Assert.IsTrue(_socket.SentMessages.Count >= 8,
            $"Expected at least 8 sent messages (6 page init + 2 perf), got {_socket.SentMessages.Count}");
    }

    [TestMethod]
    public async Task AfterNavigation_CollectsMetricsFromCdpAndObserver()
    {
        var options = new PerformanceOptions { Enable = true, CollectAfterNavigation = true };
        var hook = new PerformanceMetricsCollector(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        // Performance.enable
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        // Page.addScriptToEvaluateOnNewDocument
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"identifier": "perf-1"}}""");

        var page = (Motus.Page)await context.NewPageAsync();

        // Queue responses for AfterNavigationAsync -> CollectMetricsAsync
        // 1. Performance.getMetrics
        _socket.QueueResponse("""
            {
                "id": 11, "sessionId": "session-1",
                "result": {
                    "metrics": [
                        {"name": "Timestamp", "value": 1234.56},
                        {"name": "FirstContentfulPaint", "value": 1.5},
                        {"name": "JSHeapUsedSize", "value": 42000000},
                        {"name": "Nodes", "value": 1200}
                    ]
                }
            }
            """);

        // 2. Runtime.evaluate for __motusPerf
        _socket.QueueResponse("""
            {
                "id": 12, "sessionId": "session-1",
                "result": {
                    "result": {
                        "type": "string",
                        "value": "{\"lcp\":1800,\"fcp\":1200,\"cls\":0.05,\"inp\":120,\"layoutShifts\":[{\"score\":0.03,\"sources\":[\"DIV\"]},{\"score\":0.02,\"sources\":[\"IMG\"]}]}"
                    }
                }
            }
            """);

        // 3. Runtime.evaluate for TTFB
        _socket.QueueResponse("""
            {
                "id": 13, "sessionId": "session-1",
                "result": {
                    "result": {
                        "type": "number",
                        "value": 85.5
                    }
                }
            }
            """);

        await hook.AfterNavigationAsync(page, null);

        Assert.IsNotNull(page.LastPerformanceMetrics);
        var m = page.LastPerformanceMetrics;

        Assert.AreEqual(1800, m.Lcp, "LCP should come from PerformanceObserver");
        Assert.AreEqual(1200, m.Fcp, "FCP should prefer PerformanceObserver value over CDP");
        Assert.AreEqual(85.5, m.Ttfb, "TTFB should come from Navigation Timing API");
        Assert.AreEqual(0.05, m.Cls, "CLS should come from PerformanceObserver");
        Assert.AreEqual(120, m.Inp, "INP should come from PerformanceObserver");
        Assert.AreEqual(42000000, m.JsHeapSize, "JSHeapSize should come from CDP metrics");
        Assert.AreEqual(1200, m.DomNodeCount, "DomNodeCount should come from CDP metrics");
        Assert.IsNull(m.DiagnosticMessage);

        // Verify layout shifts
        Assert.AreEqual(2, m.LayoutShifts.Count);
        Assert.AreEqual(0.03, m.LayoutShifts[0].Score);
        Assert.AreEqual("DIV", m.LayoutShifts[0].SourceElements[0]);
        Assert.AreEqual(0.02, m.LayoutShifts[1].Score);
        Assert.AreEqual("IMG", m.LayoutShifts[1].SourceElements[0]);
    }

    [TestMethod]
    public async Task OnPageClosed_PerformsFinalCollection()
    {
        var options = new PerformanceOptions { Enable = true, CollectAfterNavigation = false };
        var hook = new PerformanceMetricsCollector(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"identifier": "perf-1"}}""");

        var page = (Motus.Page)await context.NewPageAsync();

        // Navigation should not trigger collection
        await hook.AfterNavigationAsync(page, null);
        Assert.IsNull(page.LastPerformanceMetrics);

        // Queue responses for OnPageClosedAsync -> CollectMetricsAsync
        _socket.QueueResponse("""
            {
                "id": 11, "sessionId": "session-1",
                "result": {
                    "metrics": [
                        {"name": "JSHeapUsedSize", "value": 30000000},
                        {"name": "Nodes", "value": 800}
                    ]
                }
            }
            """);
        _socket.QueueResponse("""
            {
                "id": 12, "sessionId": "session-1",
                "result": {
                    "result": {
                        "type": "string",
                        "value": "{\"lcp\":null,\"fcp\":null,\"cls\":0,\"inp\":null,\"layoutShifts\":[]}"
                    }
                }
            }
            """);
        _socket.QueueResponse("""
            {
                "id": 13, "sessionId": "session-1",
                "result": {
                    "result": {
                        "type": "object",
                        "subtype": "null",
                        "value": null
                    }
                }
            }
            """);

        await hook.OnPageClosedAsync(page);

        Assert.IsNotNull(page.LastPerformanceMetrics, "OnPageClosed should trigger final collection.");
        Assert.AreEqual(30000000, page.LastPerformanceMetrics.JsHeapSize);
        Assert.AreEqual(800, page.LastPerformanceMetrics.DomNodeCount);
    }

    private void QueuePageOnContextResponses(string targetId, string sessionId, int startId)
    {
        var id = startId;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""{targetId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
    }
}
