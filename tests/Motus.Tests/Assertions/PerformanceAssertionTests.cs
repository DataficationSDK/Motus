using Motus.Abstractions;
using Motus.Assertions;
using Motus.Tests.Transport;

namespace Motus.Tests.Assertions;

[TestClass]
public class PerformanceAssertionTests
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
    public async Task Cleanup()
    {
        PerformanceBudgetContext.Clear();
        await _transport.DisposeAsync();
    }

    private async Task<Motus.Page> CreatePageAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await _browser.NewContextAsync();

        var id = 3;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""target-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""session-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        return (Motus.Page)await context.NewPageAsync();
    }

    private static PerformanceMetrics MakeMetrics(
        double? lcp = null, double? fcp = null, double? ttfb = null,
        double? cls = null, double? inp = null,
        long? jsHeapSize = null, int? domNodeCount = null) =>
        new(Lcp: lcp, Fcp: fcp, Ttfb: ttfb, Cls: cls, Inp: inp,
            JsHeapSize: jsHeapSize, DomNodeCount: domNodeCount,
            LayoutShifts: Array.Empty<LayoutShiftEntry>(),
            CollectedAtUtc: DateTime.UtcNow);

    /// <summary>
    /// Queues Runtime.evaluate responses so RefreshPerformanceMetricsAsync completes
    /// instantly instead of blocking for the 60-second CDP command timeout.
    /// </summary>
    private void QueueRefreshResponses(int startId, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var id = startId + i;
            _socket.QueueResponse(
                $@"{{""id"": {id}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""{{}}"" }}}}}}");
        }
    }

    // ── ToMeetPerformanceBudgetAsync ──

    [TestMethod]
    public async Task ToMeetPerformanceBudget_AllWithin_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 2000, cls: 0.05);

        PerformanceBudgetContext.Push(new PerformanceBudget { Lcp = 2500, Cls = 0.1 });
        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);

        await assertions.ToMeetPerformanceBudgetAsync();
    }

    [TestMethod]
    public async Task ToMeetPerformanceBudget_LcpExceeds_Throws()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 3200, cls: 0.05);

        PerformanceBudgetContext.Push(new PerformanceBudget { Lcp = 2500, Cls = 0.1 });
        QueueRefreshResponses(9, 5);
        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToMeetPerformanceBudgetAsync(new() { Timeout = 300 }));

        Assert.IsTrue(ex.Actual!.Contains("LCP"), "Should reference LCP metric.");
        Assert.IsTrue(ex.Actual.Contains("3200"), "Should contain actual value.");
    }

    [TestMethod]
    public async Task ToMeetPerformanceBudget_MultipleFailures_FormatsAll()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 3200, cls: 0.25, inp: 350);

        PerformanceBudgetContext.Push(new PerformanceBudget { Lcp = 2500, Cls = 0.1, Inp = 200 });
        QueueRefreshResponses(9, 5);
        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToMeetPerformanceBudgetAsync(new() { Timeout = 300 }));

        Assert.IsTrue(ex.Actual!.Contains("LCP"), "Should contain LCP.");
        Assert.IsTrue(ex.Actual.Contains("CLS"), "Should contain CLS.");
        Assert.IsTrue(ex.Actual.Contains("INP"), "Should contain INP.");
    }

    [TestMethod]
    public async Task ToMeetPerformanceBudget_NoBudget_ThrowsInvalidOperation()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 2000);

        PerformanceBudgetContext.Clear();
        var assertions = new PageAssertions(page);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => assertions.ToMeetPerformanceBudgetAsync());
    }

    [TestMethod]
    public async Task ToMeetPerformanceBudget_NullMetrics_RetriesUntilTimeout()
    {
        var page = await CreatePageAsync();
        // LastPerformanceMetrics is null
        // No refresh responses queued: the 300ms assertion timeout cancels the
        // CDP send via the forwarded CancellationToken, keeping metrics null.

        PerformanceBudgetContext.Push(new PerformanceBudget { Lcp = 2500 });
        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToMeetPerformanceBudgetAsync(new() { Timeout = 300 }));

        Assert.IsTrue(ex.Actual!.Contains("no metrics collected"),
            "Should indicate metrics were not collected.");
    }

    // ── Individual metric assertions ──

    [TestMethod]
    public async Task ToHaveLcpBelow_WithinThreshold_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 2000);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.ToHaveLcpBelowAsync(2500);
    }

    [TestMethod]
    public async Task ToHaveLcpBelow_Exceeds_Throws()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 3200);

        QueueRefreshResponses(9, 5);
        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToHaveLcpBelowAsync(2500, new() { Timeout = 300 }));

        Assert.IsTrue(ex.Actual!.Contains("3200"), "Should contain actual LCP value.");
    }

    [TestMethod]
    public async Task ToHaveLcpBelow_ExactThreshold_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 2500);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.ToHaveLcpBelowAsync(2500);
    }

    [TestMethod]
    public async Task ToHaveClsBelow_WithinThreshold_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(cls: 0.05);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.ToHaveClsBelowAsync(0.1);
    }

    [TestMethod]
    public async Task ToHaveClsBelow_Exceeds_Throws()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(cls: 0.25);

        QueueRefreshResponses(9, 5);
        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToHaveClsBelowAsync(0.1, new() { Timeout = 300 }));

        Assert.IsTrue(ex.Actual!.Contains("0.250"), "Should contain actual CLS value.");
    }

    [TestMethod]
    public async Task ToHaveInpBelow_WithinThreshold_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(inp: 150);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.ToHaveInpBelowAsync(200);
    }

    [TestMethod]
    public async Task ToHaveInpBelow_Exceeds_Throws()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(inp: 350);

        QueueRefreshResponses(9, 5);
        var assertions = new PageAssertions(page);

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.ToHaveInpBelowAsync(200, new() { Timeout = 300 }));

        Assert.IsTrue(ex.Actual!.Contains("350"), "Should contain actual INP value.");
    }

    [TestMethod]
    public async Task ToHaveFcpBelow_WithinThreshold_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(fcp: 1500);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.ToHaveFcpBelowAsync(1800);
    }

    [TestMethod]
    public async Task ToHaveTtfbBelow_WithinThreshold_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(ttfb: 400);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.ToHaveTtfbBelowAsync(600);
    }

    // ── Negation ──

    [TestMethod]
    public async Task Not_ToHaveLcpBelow_WhenExceeds_Passes()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 3200);

        QueueRefreshResponses(9, 1);
        var assertions = new PageAssertions(page);
        await assertions.Not.ToHaveLcpBelowAsync(2500);
    }

    [TestMethod]
    public async Task Not_ToHaveLcpBelow_WhenWithin_Throws()
    {
        var page = await CreatePageAsync();
        page.LastPerformanceMetrics = MakeMetrics(lcp: 2000);

        QueueRefreshResponses(9, 5);
        var assertions = new PageAssertions(page);

        await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => assertions.Not.ToHaveLcpBelowAsync(2500, new() { Timeout = 300 }));
    }
}
