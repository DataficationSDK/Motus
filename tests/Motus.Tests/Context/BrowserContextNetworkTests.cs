using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class BrowserContextNetworkTests
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

    private void QueuePageOnContextResponses(string targetId, string sessionId, int startId)
    {
        var id = startId;
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""targetId"": ""{targetId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""result"": {{""sessionId"": ""{sessionId}""}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id}, ""sessionId"": ""{sessionId}"", ""result"": {{}}}}");
    }

    [TestMethod]
    public async Task SetOfflineAsync_SendsEmulateNetworkConditions()
    {
        // Use NewPageAsync on browser (creates context + page in one call)
        // This matches the pattern used by all passing page tests
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();
        var context = page.Context;

        // Queue response for Network.emulateNetworkConditions
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        await context.SetOfflineAsync(true);

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Network.emulateNetworkConditions") && json.Contains("\"offline\":true"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Network.emulateNetworkConditions with offline=true");
    }

    [TestMethod]
    public async Task SetExtraHTTPHeadersAsync_SendsSetExtraHeaders()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();
        var context = page.Context;

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        await context.SetExtraHTTPHeadersAsync(new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer token123"
        });

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Network.setExtraHTTPHeaders") && json.Contains("Bearer token123"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Network.setExtraHTTPHeaders with auth header");
    }

    /// <summary>
    /// A rule registered on the context has to turn interception on for the pages that are
    /// already open, not only for the ones opened afterwards.
    /// </summary>
    [TestMethod]
    public async Task RouteAsync_WithPageAlreadyOpen_EnablesFetchOnThatPage()
    {
        var page = await NewPageOnNewContextAsync();
        var context = page.Context;

        var before = _socket.SentMessages.Count;
        QueueGenericResponses(4);
        await context.RouteAsync("**/api/**", route => route.ContinueAsync());

        Assert.IsTrue(
            SentSince(before, "Fetch.enable", "session-1"),
            "Expected Fetch.enable on the session of the page that was already open");
    }

    /// <summary>
    /// Removing the last rule has to turn interception back off on the pages that are open.
    /// </summary>
    [TestMethod]
    public async Task UnrouteAsync_WithPageAlreadyOpen_DisablesFetchOnThatPage()
    {
        var page = await NewPageOnNewContextAsync();
        var context = page.Context;

        QueueGenericResponses(4);
        await context.RouteAsync("**/api/**", route => route.ContinueAsync());

        var before = _socket.SentMessages.Count;
        QueueGenericResponses(4);
        await context.UnrouteAsync("**/api/**");

        Assert.IsTrue(
            SentSince(before, "Fetch.disable", "session-1"),
            "Expected Fetch.disable on the session of the page that was already open");
    }

    /// <summary>
    /// Creates a context with one page in it, answering the commands that takes.
    /// </summary>
    private async Task<IPage> NewPageOnNewContextAsync()
    {
        _socket.QueueResponse("""{"id": 0, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 0, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 0, "result": {"sessionId": "session-1"}}""");
        QueueGenericResponses(8);
        return await _browser.NewPageAsync();
    }

    /// <summary>
    /// Queues empty results for commands whose answer the test does not care about. Queued
    /// responses are consumed one per outbound command, so queuing more than are needed is
    /// harmless and keeps a test from hanging on an unanswered command. The id is readdressed to
    /// the command that dequeues the response, but it has to be there for the response to be read
    /// as an answer rather than an event.
    /// </summary>
    private void QueueGenericResponses(int count)
    {
        for (var i = 0; i < count; i++)
            _socket.QueueResponse("""{"id": 0, "sessionId": "session-1", "result": {}}""");
    }

    /// <summary>Whether a command carrying both fragments was sent after the given point.</summary>
    private bool SentSince(int firstIndex, string method, string sessionId)
    {
        for (var i = firstIndex; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains(method, StringComparison.Ordinal)
                && json.Contains(sessionId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
