using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class BrowserContextStorageTests
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
    public async Task StorageStateAsync_ReturnsCookies()
    {
        QueueContextAndPageResponses();
        var page = await _browser.NewPageAsync();
        var context = page.Context;

        // Queue response for Network.getCookies (next command after page creation is ID 9)
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"cookies": [{"name": "sid", "value": "abc", "domain": ".example.com", "path": "/", "expires": -1, "size": 6, "httpOnly": true, "secure": false, "sameSite": "Lax"}]}}""");

        var state = await context.StorageStateAsync();

        Assert.AreEqual(1, state.Cookies.Count);
        Assert.AreEqual("sid", state.Cookies[0].Name);
        Assert.AreEqual("abc", state.Cookies[0].Value);
    }

    [TestMethod]
    public async Task StorageStateRestore_AddsCookies()
    {
        var cookies = new List<Cookie>
        {
            new("token", "xyz", ".test.com", "/", -1, false, true, SameSiteAttribute.None)
        };
        var storageState = new StorageState(cookies, []);
        var options = new ContextOptions { StorageState = storageState };

        // Extra response for Network.setCookie during restore
        QueueContextAndPageResponses(extraCount: 1);
        await _browser.NewPageAsync(options);

        AssertSentContains("Network.setCookie");
        AssertSentContains("token");
        AssertSentContains("xyz");
    }

    [TestMethod]
    public async Task StorageStateRestore_InjectsLocalStorage()
    {
        var origins = new List<OriginStorage>
        {
            new("https://example.com", new List<KeyValuePair<string, string>>
            {
                new("theme", "dark"),
                new("lang", "en")
            })
        };
        var storageState = new StorageState([], origins);
        var options = new ContextOptions { StorageState = storageState };

        // Extra response for Runtime.evaluate during localStorage restore
        QueueContextAndPageResponses(extraCount: 1);
        await _browser.NewPageAsync(options);

        AssertSentContains("Runtime.evaluate");
        AssertSentContains("localStorage.setItem");
    }

    [TestMethod]
    public async Task StorageStateRestore_OnlyHappensOnce()
    {
        var cookies = new List<Cookie>
        {
            new("k", "v", ".a.com", "/", -1, false, false, SameSiteAttribute.Lax)
        };
        var storageState = new StorageState(cookies, []);
        var options = new ContextOptions { StorageState = storageState };

        // Create context
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await _browser.NewContextAsync(options);

        // First page: init (4) + setCookie (1)
        QueuePageOnContextResponses("target-1", "session-1", startId: 3, extraCount: 1);
        await context.NewPageAsync();

        // Second page: init only (4), no extra setCookie
        QueuePageOnContextResponses("target-2", "session-2", startId: 10, extraCount: 0);
        await context.NewPageAsync();

        // Count setCookie calls
        var count = 0;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            if (_socket.GetSentJson(i).Contains("Network.setCookie"))
                count++;
        }
        Assert.AreEqual(1, count, "Storage state cookies should only be restored once");
    }

    // --- Helpers ---

    private void QueueContextAndPageResponses(int extraCount = 0)
    {
        var id = 2;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""browserContextId"": ""ctx-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""target-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""session-1""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        for (int i = 0; i < extraCount; i++)
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""success"": true}}}}");
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
            _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{""success"": true}}}}");
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
