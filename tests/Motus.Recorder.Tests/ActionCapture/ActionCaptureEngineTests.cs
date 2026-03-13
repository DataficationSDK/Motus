using System.Text.Json;
using Motus.Abstractions;
using Motus.Recorder.ActionCapture;
using Motus.Recorder.Records;
using Motus.Recorder.Tests.Transport;

namespace Motus.Recorder.Tests.ActionCapture;

[TestClass]
public class ActionCaptureEngineTests
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

    private async Task<IPage> CreatePageAndStartEngineAsync(ActionCaptureEngine engine)
    {
        var page = await CreatePageAsync();

        // Queue responses for: Runtime.addBinding, Page.addScriptToEvaluateOnNewDocument, Runtime.evaluate
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {"identifier": "1"}}""");
        _socket.QueueResponse("""{"id": 11, "sessionId": "session-1", "result": {"result": {"type": "undefined"}}}""");

        await engine.StartAsync(page);
        return page;
    }

    /// <summary>
    /// Builds the CDP Runtime.bindingCalled event JSON that the transport expects.
    /// The payload field is a JSON string containing a JSON array of binding arguments.
    /// </summary>
    private static string BuildBindingCallEvent(string bindingName, string innerJson)
    {
        // payload must be a JSON string whose value is a JSON array: ["innerJson"]
        var payloadValue = JsonSerializer.Serialize(new[] { innerJson });
        var escapedPayload = JsonSerializer.Serialize(payloadValue); // double-serialize for embedding in JSON string

        return $$"""
            {
                "method": "Runtime.bindingCalled",
                "sessionId": "session-1",
                "params": {
                    "name": "{{bindingName}}",
                    "payload": {{escapedPayload}},
                    "executionContextId": 1
                }
            }
            """;
    }

    [TestMethod]
    public async Task StartAsync_RegistersBindingAndInjectsScript()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        Assert.IsTrue(engine.IsRecording);

        var sentMessages = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        var addBindingMsg = sentMessages.FirstOrDefault(m => m.Contains("Runtime.addBinding"));
        Assert.IsNotNull(addBindingMsg, "Runtime.addBinding should have been sent");
        Assert.IsTrue(addBindingMsg.Contains("__motus_recorder__"));

        var addScriptMsg = sentMessages.FirstOrDefault(m => m.Contains("Page.addScriptToEvaluateOnNewDocument"));
        Assert.IsNotNull(addScriptMsg, "Page.addScriptToEvaluateOnNewDocument should have been sent");

        await engine.StopAsync();
    }

    [TestMethod]
    public async Task BindingCallback_ClickPayload_EmitsClickAction()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        // Simulate mousedown via binding
        _socket.Enqueue(BuildBindingCallEvent("__motus_recorder__",
            """{"type":"mousedown","timestamp":1710000000000,"x":100,"y":200,"button":"left","clickCount":1,"modifiers":0,"pageUrl":"https://example.com"}"""));

        await Task.Delay(200);

        // Simulate mouseup via binding
        _socket.Enqueue(BuildBindingCallEvent("__motus_recorder__",
            """{"type":"mouseup","timestamp":1710000000050,"x":101,"y":201,"button":"left","modifiers":0,"pageUrl":"https://example.com"}"""));

        await Task.Delay(200);

        await engine.StopAsync();

        var clickAction = engine.CapturedActions.OfType<ClickAction>().FirstOrDefault();
        Assert.IsNotNull(clickAction, "Should have captured a ClickAction");
        Assert.AreEqual("left", clickAction.Button);
    }

    [TestMethod]
    public async Task MultipleInputPayloads_DebounceIntoSingleFillAction()
    {
        // Use a long debounce window so the timer never fires mid-test.
        // StopAsync() calls Flush() which emits the pending fill.
        var options = new ActionCaptureOptions { FillDebounceMs = 2000 };
        await using var engine = new ActionCaptureEngine(options);
        await CreatePageAndStartEngineAsync(engine);

        _socket.Enqueue(BuildBindingCallEvent("__motus_recorder__",
            """{"type":"input","timestamp":1710000000000,"x":10,"y":20,"value":"h","pageUrl":"https://example.com"}"""));
        _socket.Enqueue(BuildBindingCallEvent("__motus_recorder__",
            """{"type":"input","timestamp":1710000000020,"x":10,"y":20,"value":"he","pageUrl":"https://example.com"}"""));
        _socket.Enqueue(BuildBindingCallEvent("__motus_recorder__",
            """{"type":"input","timestamp":1710000000040,"x":10,"y":20,"value":"hel","pageUrl":"https://example.com"}"""));

        // Wait for all binding callbacks to be dispatched and processed
        await Task.Delay(500);

        // StopAsync flushes the pending fill (timer hasn't fired due to 2s window)
        await engine.StopAsync();

        var fillActions = engine.CapturedActions.OfType<FillAction>().ToList();
        Assert.AreEqual(1, fillActions.Count, "Should have a single debounced FillAction");
        Assert.AreEqual("hel", fillActions[0].Value);
    }

    [TestMethod]
    public async Task CdpFrameNavigated_EmitsNavigationAction()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        // Simulate main frame navigation via the Page's internal event
        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "main-frame",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "https://example.com/page2",
                        "securityOrigin": "https://example.com",
                        "mimeType": "text/html"
                    }
                }
            }
            """);

        await Task.Delay(300);

        var captured = engine.CapturedActions;
        await engine.StopAsync();

        var nav = captured.OfType<NavigationAction>().FirstOrDefault();
        Assert.IsNotNull(nav, "Should have captured a NavigationAction");
        Assert.AreEqual("https://example.com/page2", nav.Url);
    }

    [TestMethod]
    public async Task StopAsync_SetsIsRecordingFalse()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        await engine.StopAsync();

        Assert.IsFalse(engine.IsRecording);
    }

    [TestMethod]
    public async Task CapturedActions_PopulatedByNavigationEvent()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "main-frame",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "https://example.com/test",
                        "securityOrigin": "https://example.com",
                        "mimeType": "text/html"
                    }
                }
            }
            """);

        await Task.Delay(300);

        var captured = engine.CapturedActions;
        await engine.StopAsync();

        Assert.IsTrue(captured.Count > 0,
            "CapturedActions should contain the navigation action");
    }
}
