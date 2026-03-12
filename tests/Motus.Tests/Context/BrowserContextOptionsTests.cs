using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class BrowserContextOptionsTests
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

        _browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task Viewport_SendsDeviceMetricsOverride()
    {
        var options = new ContextOptions { Viewport = new ViewportSize(1024, 768) };
        QueueContextAndPageResponses(extraEmulationCount: 1);

        var page = await CreatePageWithOptions(options);

        AssertSentContains("Emulation.setDeviceMetricsOverride");
        Assert.AreEqual(1024, page.ViewportSize?.Width);
        Assert.AreEqual(768, page.ViewportSize?.Height);
    }

    [TestMethod]
    public async Task Locale_SendsLocaleOverride()
    {
        var options = new ContextOptions { Locale = "fr-FR" };
        QueueContextAndPageResponses(extraEmulationCount: 1);

        await CreatePageWithOptions(options);

        AssertSentContains("Emulation.setLocaleOverride");
        AssertSentContains("fr-FR");
    }

    [TestMethod]
    public async Task TimezoneId_SendsTimezoneOverride()
    {
        var options = new ContextOptions { TimezoneId = "America/New_York" };
        QueueContextAndPageResponses(extraEmulationCount: 1);

        await CreatePageWithOptions(options);

        AssertSentContains("Emulation.setTimezoneOverride");
        AssertSentContains("America/New_York");
    }

    [TestMethod]
    public async Task ColorScheme_SendsEmulatedMedia()
    {
        var options = new ContextOptions { ColorScheme = ColorScheme.Dark };
        QueueContextAndPageResponses(extraEmulationCount: 1);

        await CreatePageWithOptions(options);

        AssertSentContains("Emulation.setEmulatedMedia");
        AssertSentContains("prefers-color-scheme");
        AssertSentContains("dark");
    }

    [TestMethod]
    public async Task UserAgent_SendsUserAgentOverride()
    {
        var options = new ContextOptions { UserAgent = "CustomAgent/1.0" };
        QueueContextAndPageResponses(extraEmulationCount: 1);

        await CreatePageWithOptions(options);

        AssertSentContains("Emulation.setUserAgentOverride");
        AssertSentContains("CustomAgent/1.0");
    }

    [TestMethod]
    public async Task IgnoreHTTPSErrors_SendsSecurityCommands()
    {
        var options = new ContextOptions { IgnoreHTTPSErrors = true };
        // Security.enable + Security.setIgnoreCertificateErrors = 2 extra
        QueueContextAndPageResponses(extraEmulationCount: 2);

        await CreatePageWithOptions(options);

        AssertSentContains("Security.enable");
        AssertSentContains("Security.setIgnoreCertificateErrors");
    }

    [TestMethod]
    public async Task Proxy_PassesProxyServerInCreateBrowserContext()
    {
        var options = new ContextOptions
        {
            Proxy = new ProxySettings(Server: "http://proxy:8080")
        };
        QueueContextAndPageResponses(extraEmulationCount: 0);

        await CreatePageWithOptions(options);

        AssertSentContains("Target.createBrowserContext");
        AssertSentContains("http://proxy:8080");
    }

    [TestMethod]
    public async Task BaseURL_ResolvesRelativeUrl()
    {
        var options = new ContextOptions { BaseURL = "https://example.com" };
        QueueContextAndPageResponses(extraEmulationCount: 0);

        var page = await CreatePageWithOptions(options);

        // Queue response for Page.navigate -- navigation will return immediately with a frameId
        _socket.QueueResponse("""{"id": 100, "sessionId": "session-1", "result": {"frameId": "f1"}}""");

        // Start GotoAsync and immediately fire load event so it completes
        var gotoTask = page.GotoAsync("/api/data");
        _socket.Enqueue("""{"sessionId": "session-1", "method": "Page.loadEventFired", "params": {"timestamp": 1.0}}""");

        try { await gotoTask.WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { /* acceptable -- we just need to verify the sent URL */ }

        // Check the Page.navigate command used the resolved URL
        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Page.navigate") && json.Contains("https://example.com/api/data"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Page.navigate with resolved URL https://example.com/api/data");
    }

    [TestMethod]
    public async Task Permissions_GrantedOnContextCreation()
    {
        var options = new ContextOptions
        {
            Permissions = ["geolocation", "notifications"]
        };
        // Extra response for Browser.grantPermissions
        QueueContextAndPageResponses(extraEmulationCount: 0, extraContextCount: 1);

        await CreatePageWithOptions(options);

        AssertSentContains("Browser.grantPermissions");
        AssertSentContains("geolocation");
    }

    [TestMethod]
    public async Task RecordVideo_AcceptedWithoutError()
    {
        var options = new ContextOptions
        {
            RecordVideo = new RecordVideoOptions { Dir = "/tmp/videos" }
        };
        QueueContextAndPageResponses(extraEmulationCount: 0);

        // Should not throw
        var page = await CreatePageWithOptions(options);
        Assert.IsNotNull(page);
    }

    [TestMethod]
    public async Task Tracing_ReturnsWithoutThrowing()
    {
        QueueContextAndPageResponses(extraEmulationCount: 0);
        var page = await CreatePageWithOptions(null);

        var tracing = page.Context.Tracing;
        Assert.IsNotNull(tracing);

        // StartAsync and StopAsync should be no-ops
        await tracing.StartAsync();
        await tracing.StopAsync();
    }

    [TestMethod]
    public async Task MultiPage_BothReceiveEmulationCommands()
    {
        var options = new ContextOptions { Locale = "de-DE" };

        // Create context
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await _browser.NewContextAsync(options);

        // First page: 4 init commands + 1 emulation
        QueuePageOnContextResponses("target-1", "session-1", startId: 3, extraCount: 1);
        var page1 = await context.NewPageAsync();

        // Second page: 4 init commands + 1 emulation
        QueuePageOnContextResponses("target-2", "session-2", startId: 10, extraCount: 1);
        var page2 = await context.NewPageAsync();

        // Count locale override commands
        var count = 0;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Emulation.setLocaleOverride") && json.Contains("de-DE"))
                count++;
        }
        Assert.AreEqual(2, count, "Expected two Emulation.setLocaleOverride commands (one per page)");
    }

    // --- Helpers ---

    private async Task<Motus.Abstractions.IPage> CreatePageWithOptions(ContextOptions? options)
    {
        var page = await _browser.NewPageAsync(options);
        return page;
    }

    private void QueueContextAndPageResponses(int extraEmulationCount, int extraContextCount = 0)
    {
        var id = 2;
        // Target.createBrowserContext
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""browserContextId"": ""ctx-1""}}}}");

        // Extra context-level commands (e.g., Browser.grantPermissions)
        for (int i = 0; i < extraContextCount; i++)
            _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{}}}}");

        // Target.createTarget
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""target-1""}}}}");
        // Target.attachToTarget
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""session-1""}}}}");
        // Page.enable
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        // Runtime.enable
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        // Page.setInterceptFileChooserDialog
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        // Network.enable
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        // Extra emulation commands
        for (int i = 0; i < extraEmulationCount; i++)
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
    }

    private void QueuePageOnContextResponses(string targetId, string sessionId, int startId, int extraCount = 0)
    {
        var id = startId;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""{targetId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");

        for (int i = 0; i < extraCount; i++)
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
    }

    private void AssertSentContains(string expected)
    {
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            if (_socket.GetSentJson(i).Contains(expected))
                return;
        }
        Assert.Fail($"Expected sent messages to contain '{expected}'");
    }
}
