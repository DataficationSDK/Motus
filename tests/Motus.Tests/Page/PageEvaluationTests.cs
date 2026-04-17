using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

[TestClass]
public class PageEvaluationTests
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

    private async Task<Motus.Abstractions.IPage> CreatePageWithFrameAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        var page = await _browser.NewPageAsync();

        // Inject frame navigated + execution context
        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "about:blank"
                    }
                }
            }
            """);

        _socket.Enqueue("""
            {
                "method": "Runtime.executionContextCreated",
                "sessionId": "session-1",
                "params": {
                    "context": {
                        "id": 1,
                        "origin": "://",
                        "name": "",
                        "auxData": { "frameId": "frame-main", "isDefault": true }
                    }
                }
            }
            """);

        await Task.Delay(100);

        return page;
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsStringResult()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<string>("document.title");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "string", "value": "Test Page" }
                }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual("Test Page", result);
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsNumberResult()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<int>("1 + 1");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "number", "value": 2 }
                }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual(2, result);
    }

    [TestMethod]
    public async Task EvaluateAsync_ThrowsOnJsException()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<string>("throw new Error('boom')");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "undefined" },
                    "exceptionDetails": {
                        "exceptionId": 1,
                        "text": "Error: boom",
                        "lineNumber": 0,
                        "columnNumber": 0
                    }
                }
            }
            """);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => evalTask);
    }

    [TestMethod]
    public async Task EvaluateHandleAsync_ReturnsJsHandle()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateHandleAsync("document.body");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": {
                    "result": { "type": "object", "objectId": "obj-123" }
                }
            }
            """);

        var handle = await evalTask;
        Assert.IsNotNull(handle);
    }

    [TestMethod]
    public async Task EvaluateAsync_BareExpression_PassesThroughUnwrapped()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<string>("document.title");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "result": { "type": "string", "value": "T" } }
            }
            """);

        await evalTask;

        var sentExpression = GetEvaluateExpression(_socket.GetSentJson(8));
        Assert.AreEqual("document.title", sentExpression);
    }

    [TestMethod]
    public async Task EvaluateAsync_ArrowFunction_WrappedAsIIFE()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<int>("() => 42");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "result": { "type": "number", "value": 42 } }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual(42, result);

        var sentExpression = GetEvaluateExpression(_socket.GetSentJson(8));
        Assert.AreEqual("((() => 42)(undefined))", sentExpression);
    }

    [TestMethod]
    public async Task EvaluateAsync_FunctionLiteralWithArg_InvokesWithSerializedArg()
    {
        var page = await CreatePageWithFrameAsync();

        var evalTask = page.EvaluateAsync<int>("function(x) { return x * 2; }", arg: 5);

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "result": { "type": "number", "value": 10 } }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual(10, result);

        var sentExpression = GetEvaluateExpression(_socket.GetSentJson(8));
        Assert.AreEqual("((function(x) { return x * 2; })(5))", sentExpression);
    }

    [TestMethod]
    public async Task EvaluateAsync_NonFunctionWithArg_Throws()
    {
        var page = await CreatePageWithFrameAsync();

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => page.EvaluateAsync<int>("1 + 1", arg: 5));
    }

    [TestMethod]
    public async Task WaitForFunctionAsync_ArrowFunction_InvokesAndReturnsBool()
    {
        var page = await CreatePageWithFrameAsync();

        var waitTask = page.WaitForFunctionAsync<bool>("() => window.ready === true");

        _socket.Enqueue("""
            {
                "id": 9,
                "sessionId": "session-1",
                "result": { "result": { "type": "boolean", "value": true } }
            }
            """);

        var result = await waitTask;
        Assert.IsTrue(result);

        var sentExpression = GetEvaluateExpression(_socket.GetSentJson(8));
        Assert.AreEqual("((() => window.ready === true)(undefined))", sentExpression);
    }

    [TestMethod]
    public async Task WaitForFunctionAsync_PollsUntilTruthy()
    {
        var page = await CreatePageWithFrameAsync();

        _socket.QueueResponse("""
            {"id": 9, "sessionId": "session-1",
             "result": { "result": { "type": "boolean", "value": false } }}
            """);
        _socket.QueueResponse("""
            {"id": 10, "sessionId": "session-1",
             "result": { "result": { "type": "boolean", "value": false } }}
            """);
        _socket.QueueResponse("""
            {"id": 11, "sessionId": "session-1",
             "result": { "result": { "type": "boolean", "value": true } }}
            """);

        var waitTask = page.WaitForFunctionAsync<bool>("() => window.ready", timeout: 5_000);

        var result = await waitTask;
        Assert.IsTrue(result);

        // Confirm the loop issued more than one Runtime.evaluate.
        Assert.IsTrue(_socket.SentMessages.Count >= 10,
            $"Expected at least 10 sent messages (8 setup + 2+ polls), got {_socket.SentMessages.Count}");
    }

    [TestMethod]
    public async Task WaitForFunctionAsync_TimesOut()
    {
        var page = await CreatePageWithFrameAsync();

        for (int i = 0; i < 10; i++)
        {
            var id = 9 + i;
            _socket.QueueResponse(
                "{\"id\": " + id + ", \"sessionId\": \"session-1\", " +
                "\"result\": { \"result\": { \"type\": \"boolean\", \"value\": false } } }");
        }

        await Assert.ThrowsExceptionAsync<TimeoutException>(
            () => page.WaitForFunctionAsync<bool>("() => false", timeout: 200));
    }

    private static string GetEvaluateExpression(string sentJson)
    {
        using var doc = JsonDocument.Parse(sentJson);
        return doc.RootElement.GetProperty("params").GetProperty("expression").GetString()!;
    }

    [DataTestMethod]
    [DataRow("() => window.x", true)]
    [DataRow("x => x + 1", true)]
    [DataRow("(a, b) => a + b", true)]
    [DataRow("async () => await fetch('/')", true)]
    [DataRow("async x => x", true)]
    [DataRow("function() { return 1; }", true)]
    [DataRow("function named(x) { return x; }", true)]
    [DataRow("async function() { return 1; }", true)]
    [DataRow("  () => 1", true)]
    [DataRow("document.title", false)]
    [DataRow("1 + 1", false)]
    [DataRow("window.ready === true", false)]
    [DataRow("window.getConfig", false)]
    [DataRow("'arrow in string =>'", false)]
    [DataRow("(() => 1)()", false)]
    [DataRow("(async () => 1)()", false)]
    [DataRow("((x) => x + 1)(5)", false)]
    [DataRow("(function() { return 1; })()", false)]
    public void LooksLikeFunctionExpression_Classifies(string expression, bool expected)
    {
        Assert.AreEqual(expected, Motus.Page.LooksLikeFunctionExpression(expression));
    }
}
