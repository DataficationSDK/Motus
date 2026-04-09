using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Performance;

[TestClass]
public class PerformanceMetricsCollectorTests
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
    public async Task HookDisabled_DoesNotRegisterAsLifecycleHook()
    {
        var options = new PerformanceOptions { Enable = false };
        var hook = new PerformanceMetricsCollector(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();

        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        Assert.IsNull(page.LastPerformanceMetrics,
            "Disabled hook should not have collected any metrics on page creation.");
    }

    [TestMethod]
    public async Task HookEnabled_RegistersAsLifecycleHook()
    {
        var options = new PerformanceOptions { Enable = true };
        var hook = new PerformanceMetricsCollector(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();

        await hook.OnLoadedAsync(pluginContext);

        Assert.AreEqual("motus.performance-metrics", hook.PluginId);
        Assert.AreEqual("Performance Metrics Collector", hook.Name);
    }

    [TestMethod]
    public void NullOptions_DefaultsToDisabled()
    {
        var hook = new PerformanceMetricsCollector(null);
        Assert.AreEqual("motus.performance-metrics", hook.PluginId);
    }

    [TestMethod]
    public async Task CollectAfterNavigation_False_SkipsCollection()
    {
        var options = new PerformanceOptions { Enable = true, CollectAfterNavigation = false };
        var hook = new PerformanceMetricsCollector(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        // AfterNavigation should not collect when CollectAfterNavigation is false
        await hook.AfterNavigationAsync(page, null);

        Assert.IsNull(page.LastPerformanceMetrics,
            "CollectAfterNavigation=false should skip metric collection.");
    }

    [TestMethod]
    public void PluginMetadata_IsCorrect()
    {
        var hook = new PerformanceMetricsCollector(new PerformanceOptions());
        Assert.AreEqual("motus.performance-metrics", hook.PluginId);
        Assert.AreEqual("Performance Metrics Collector", hook.Name);
        Assert.AreEqual("1.0.0", hook.Version);
        Assert.AreEqual("Motus", hook.Author);
    }

    [TestMethod]
    public async Task OnUnloadedAsync_CompletesSuccessfully()
    {
        var hook = new PerformanceMetricsCollector(new PerformanceOptions());
        await hook.OnUnloadedAsync(); // should not throw
    }

    [TestMethod]
    public async Task NoOpHookMethods_CompleteSuccessfully()
    {
        var hook = new PerformanceMetricsCollector(new PerformanceOptions());

        // These should all be no-ops and complete without error
        await hook.BeforeNavigationAsync(null!, "https://example.com");
        await hook.BeforeActionAsync(null!, "click");
        await hook.AfterActionAsync(null!, "click", new ActionResult("test"));
        await hook.OnConsoleMessageAsync(null!, new ConsoleMessageEventArgs("log", "text"));
        await hook.OnPageErrorAsync(null!, new PageErrorEventArgs("error"));
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
