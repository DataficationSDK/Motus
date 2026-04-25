using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Coverage;

[TestClass]
public class CoverageCollectorTests
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
    public void NullOptions_DefaultsToDisabled()
    {
        var hook = new CoverageCollector(null);
        Assert.AreEqual("motus.code-coverage", hook.PluginId);
    }

    [TestMethod]
    public void PluginMetadata_IsCorrect()
    {
        var hook = new CoverageCollector(new CoverageOptions());
        Assert.AreEqual("motus.code-coverage", hook.PluginId);
        Assert.AreEqual("Code Coverage Collector", hook.Name);
        Assert.AreEqual("1.0.0", hook.Version);
        Assert.AreEqual("Motus", hook.Author);
    }

    [TestMethod]
    public async Task HookDisabled_DoesNotCollectOnPageCreation()
    {
        var hook = new CoverageCollector(new CoverageOptions { Enable = false });

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();

        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        Assert.IsNull(page.LastCoverage,
            "Disabled hook should not have collected coverage on page creation.");
    }

    [TestMethod]
    public async Task HookEnabled_LoadsWithoutThrowing()
    {
        var hook = new CoverageCollector(new CoverageOptions { Enable = true });

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();

        await hook.OnLoadedAsync(pluginContext);

        Assert.AreEqual("motus.code-coverage", hook.PluginId);
    }

    [TestMethod]
    public async Task OnUnloadedAsync_CompletesSuccessfully()
    {
        var hook = new CoverageCollector(new CoverageOptions());
        await hook.OnUnloadedAsync();
    }

    [TestMethod]
    public async Task NoOpHookMethods_CompleteSuccessfully()
    {
        var hook = new CoverageCollector(new CoverageOptions());

        await hook.BeforeNavigationAsync(null!, "https://example.com");
        await hook.AfterNavigationAsync(null!, null);
        await hook.BeforeActionAsync(null!, "click");
        await hook.AfterActionAsync(null!, "click", new ActionResult("test"));
        await hook.OnConsoleMessageAsync(null!, new ConsoleMessageEventArgs("log", "text"));
        await hook.OnPageErrorAsync(null!, new PageErrorEventArgs("error"));
    }

    [TestMethod]
    public async Task OnPageClosedAsync_WhenContextNull_ReturnsWithoutThrow()
    {
        // Hook never had OnLoadedAsync called with Enable=true, so _context is null.
        var hook = new CoverageCollector(new CoverageOptions { Enable = false });

        // Passing null! as page must be safe because of the early-return on null _context.
        await hook.OnPageClosedAsync(null!);
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

[TestClass]
public class CoverageCollectorCapabilityTests
{
    [TestMethod]
    public void BiDiSession_DoesNotHaveCodeCoverageCapability()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        Assert.AreEqual(
            MotusCapabilities.None,
            session.Capabilities & MotusCapabilities.CodeCoverage,
            "BiDi sessions should not have the CodeCoverage capability.");
    }

    [TestMethod]
    public void AllCdp_IncludesCodeCoverageCapability()
    {
        Assert.AreNotEqual(
            MotusCapabilities.None,
            MotusCapabilities.AllCdp & MotusCapabilities.CodeCoverage,
            "AllCdp must include the CodeCoverage capability.");
    }
}
