using Motus.Abstractions;
using Motus.Assertions;
using Motus.Tests.Transport;

namespace Motus.Tests.Assertions;

[TestClass]
public class LocatorAssertionsTests
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

    private async Task<IPage> CreatePageAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    private void QueueStrategyResolve(ref int id, string objectId = "elem-1")
    {
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-{objectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{objectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
    }

    private void QueueResolveAndEval(ref int id, string objectId, string valueJson)
    {
        QueueStrategyResolve(ref id, objectId);
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {valueJson}}}}}");
    }

    // --- ToBeVisibleAsync ---

    [TestMethod]
    public async Task ToBeVisibleAsync_Passes_WhenVisible()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        var id = 9;
        QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToBeVisibleAsync(new() { Timeout = 200 });
    }

    [TestMethod]
    public async Task ToBeVisibleAsync_Throws_WhenHidden()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        // Queue repeated "false" responses for polling
        for (var i = 0; i < 10; i++)
        {
            var id = 9 + i * 3;
            QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": false}""");
        }

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(locator).ToBeVisibleAsync(new() { Timeout = 200 }));
        Assert.IsTrue(ex.Message.Contains("ToBeVisible"));
    }

    [TestMethod]
    public async Task Not_ToBeVisibleAsync_Passes_WhenHidden()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        var id = 9;
        QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": false}""");

        await Expect.That(locator).Not.ToBeVisibleAsync(new() { Timeout = 200 });
    }

    [TestMethod]
    public async Task Not_ToBeVisibleAsync_Throws_WhenVisible()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        for (var i = 0; i < 10; i++)
        {
            var id = 9 + i * 3;
            QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": true}""");
        }

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(locator).Not.ToBeVisibleAsync(new() { Timeout = 200 }));
        Assert.IsTrue(ex.Message.Contains("NOT"));
    }

    // --- ToHaveTextAsync ---

    [TestMethod]
    public async Task ToHaveTextAsync_Passes_WhenExactMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        var id = 9;
        QueueResolveAndEval(ref id, "elem-1", """{"type": "string", "value": "Hello World"}""");

        await Expect.That(locator).ToHaveTextAsync("Hello World", new() { Timeout = 200 });
    }

    [TestMethod]
    public async Task ToHaveTextAsync_Throws_WhenNoMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        for (var i = 0; i < 10; i++)
        {
            var id = 9 + i * 3;
            QueueResolveAndEval(ref id, "elem-1", """{"type": "string", "value": "Wrong Text"}""");
        }

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(locator).ToHaveTextAsync("Hello World", new() { Timeout = 200 }));
        Assert.AreEqual("Wrong Text", ex.Actual);
        Assert.IsTrue(ex.Message.Contains("ToHaveText"));
    }

    // --- ToContainTextAsync ---

    [TestMethod]
    public async Task ToContainTextAsync_Passes_WhenContains()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        var id = 9;
        QueueResolveAndEval(ref id, "elem-1", """{"type": "string", "value": "Hello World"}""");

        await Expect.That(locator).ToContainTextAsync("World", new() { Timeout = 200 });
    }

    [TestMethod]
    public async Task ToContainTextAsync_Throws_WhenMissing()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        for (var i = 0; i < 10; i++)
        {
            var id = 9 + i * 3;
            QueueResolveAndEval(ref id, "elem-1", """{"type": "string", "value": "Hello World"}""");
        }

        await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(locator).ToContainTextAsync("Goodbye", new() { Timeout = 200 }));
    }

    // --- ToHaveTextAsync (Regex) ---

    [TestMethod]
    public async Task ToHaveTextAsync_Regex_Passes_WhenMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#title");

        var id = 9;
        QueueResolveAndEval(ref id, "elem-1", """{"type": "string", "value": "Price: $42.00"}""");

        await Expect.That(locator).ToHaveTextAsync(
            new System.Text.RegularExpressions.Regex(@"Price: \$\d+\.\d{2}"),
            new() { Timeout = 200 });
    }

    // --- ToBeEnabledAsync ---

    [TestMethod]
    public async Task ToBeEnabledAsync_Passes_WhenEnabled()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        var id = 9;
        QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToBeEnabledAsync(new() { Timeout = 200 });
    }

    // --- ToBeDisabledAsync ---

    [TestMethod]
    public async Task ToBeDisabledAsync_Passes_WhenDisabled()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        var id = 9;
        QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToBeDisabledAsync(new() { Timeout = 200 });
    }

    // --- ToBeCheckedAsync ---

    [TestMethod]
    public async Task ToBeCheckedAsync_Passes_WhenChecked()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#cb");

        var id = 9;
        QueueResolveAndEval(ref id, "cb-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToBeCheckedAsync(new() { Timeout = 200 });
    }

    // --- ToHaveCountAsync ---

    [TestMethod]
    public async Task ToHaveCountAsync_Passes_WhenMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("li.item");

        // CountAsync uses ResolveAllHandlesAsync (strategy pattern: evaluate + getProperties)
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {"result": {"type": "object", "objectId": "arr-1"}}}""");
        _socket.QueueResponse("""
            {
                "id": 10, "sessionId": "session-1",
                "result": {
                    "result": [
                        {"name": "0", "value": {"type": "object", "objectId": "e-0"}},
                        {"name": "1", "value": {"type": "object", "objectId": "e-1"}},
                        {"name": "2", "value": {"type": "object", "objectId": "e-2"}},
                        {"name": "length", "value": {"type": "number", "value": 3}}
                    ]
                }
            }
            """);

        await Expect.That(locator).ToHaveCountAsync(3, new() { Timeout = 200 });
    }

    // --- ToHaveValueAsync ---

    [TestMethod]
    public async Task ToHaveValueAsync_Passes_WhenMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#input");

        var id = 9;
        QueueResolveAndEval(ref id, "inp-1", """{"type": "string", "value": "test@email.com"}""");

        await Expect.That(locator).ToHaveValueAsync("test@email.com", new() { Timeout = 200 });
    }

    // --- ToHaveAttributeAsync ---

    [TestMethod]
    public async Task ToHaveAttributeAsync_Passes_WhenMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#link");

        var id = 9;
        QueueResolveAndEval(ref id, "link-1", """{"type": "string", "value": "https://example.com"}""");

        await Expect.That(locator).ToHaveAttributeAsync("href", "https://example.com", new() { Timeout = 200 });
    }

    // --- ToHaveClassAsync ---

    [TestMethod]
    public async Task ToHaveClassAsync_Passes_WhenHasClass()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#div");

        var id = 9;
        QueueResolveAndEval(ref id, "div-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToHaveClassAsync("active", new() { Timeout = 200 });
    }

    // --- ToHaveCSSAsync ---

    [TestMethod]
    public async Task ToHaveCSSAsync_Passes_WhenMatch()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#div");

        var id = 9;
        QueueResolveAndEval(ref id, "div-1", """{"type": "string", "value": "none"}""");

        await Expect.That(locator).ToHaveCSSAsync("display", "none", new() { Timeout = 200 });
    }

    // --- ToBeEmptyAsync ---

    [TestMethod]
    public async Task ToBeEmptyAsync_Passes_WhenEmpty()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#input");

        var id = 9;
        QueueResolveAndEval(ref id, "inp-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToBeEmptyAsync(new() { Timeout = 200 });
    }

    // --- ToBeEditableAsync ---

    [TestMethod]
    public async Task ToBeEditableAsync_Passes_WhenEditable()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#input");

        var id = 9;
        QueueResolveAndEval(ref id, "inp-1", """{"type": "boolean", "value": true}""");

        await Expect.That(locator).ToBeEditableAsync(new() { Timeout = 200 });
    }

    // --- Custom message ---

    [TestMethod]
    public async Task CustomMessage_AppearsInException()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        for (var i = 0; i < 10; i++)
        {
            var id = 9 + i * 3;
            QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": false}""");
        }

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(locator).ToBeVisibleAsync(new() { Timeout = 200, Message = "Button should be visible after login" }));
        Assert.AreEqual("Button should be visible after login", ex.Message);
    }

    // --- Timeout override ---

    [TestMethod]
    public async Task Timeout_Override_IsRespected()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        // Queue enough responses for the short polling window
        for (var i = 0; i < 5; i++)
        {
            var id = 9 + i * 3;
            QueueResolveAndEval(ref id, "btn-1", """{"type": "boolean", "value": false}""");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(locator).ToBeVisibleAsync(new() { Timeout = 150 }));
        sw.Stop();

        // Should timeout around 150ms, not 30s
        Assert.IsTrue(sw.ElapsedMilliseconds < 2000, $"Took {sw.ElapsedMilliseconds}ms, expected ~150ms");
    }
}
