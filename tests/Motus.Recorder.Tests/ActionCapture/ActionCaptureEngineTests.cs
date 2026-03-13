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

    // ---- Wiring tests (verify CDP commands sent) ----

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
    public async Task StopAsync_SetsIsRecordingFalse()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        await engine.StopAsync();

        Assert.IsFalse(engine.IsRecording);
    }

    // ---- Direct payload injection tests (deterministic, no CDP binding path) ----
    // The binding callback path uses Task.Run (fire-and-forget) with catch-all error
    // suppression in Page.OnBindingCalled, making it unreliable for unit tests.
    // ProcessDomEvent bypasses that path and tests the engine pipeline directly.

    [TestMethod]
    public async Task ProcessDomEvent_ClickSequence_EmitsClickAction()
    {
        await using var engine = new ActionCaptureEngine();
        await CreatePageAndStartEngineAsync(engine);

        engine.ProcessDomEvent(
            """{"type":"mousedown","timestamp":1710000000000,"x":100,"y":200,"button":"left","clickCount":1,"modifiers":0,"pageUrl":"https://example.com"}""");
        engine.ProcessDomEvent(
            """{"type":"mouseup","timestamp":1710000000050,"x":101,"y":201,"button":"left","modifiers":0,"pageUrl":"https://example.com"}""");

        await engine.StopAsync();

        var click = engine.CapturedActions.OfType<ClickAction>().FirstOrDefault();
        Assert.IsNotNull(click, "Should have captured a ClickAction");
        Assert.AreEqual("left", click.Button);
        Assert.AreEqual(1, click.ClickCount);
        Assert.AreEqual(100.0, click.X);
        Assert.AreEqual(200.0, click.Y);
    }

    [TestMethod]
    public async Task ProcessDomEvent_MultipleInputs_DebounceIntoSingleFill()
    {
        var options = new ActionCaptureOptions { FillDebounceMs = 5000 };
        await using var engine = new ActionCaptureEngine(options);
        await CreatePageAndStartEngineAsync(engine);

        engine.ProcessDomEvent(
            """{"type":"input","timestamp":1710000000000,"x":10,"y":20,"value":"h","pageUrl":"https://example.com"}""");
        engine.ProcessDomEvent(
            """{"type":"input","timestamp":1710000000020,"x":10,"y":20,"value":"he","pageUrl":"https://example.com"}""");
        engine.ProcessDomEvent(
            """{"type":"input","timestamp":1710000000040,"x":10,"y":20,"value":"hel","pageUrl":"https://example.com"}""");

        // StopAsync flushes the pending fill (timer hasn't fired due to 5s window)
        await engine.StopAsync();

        var fills = engine.CapturedActions.OfType<FillAction>().ToList();
        Assert.AreEqual(1, fills.Count, "Should have a single debounced FillAction");
        Assert.AreEqual("hel", fills[0].Value);
    }

    [TestMethod]
    public async Task ProcessDomEvent_BlurFlushesFill_ThenCapturedActionsContainsIt()
    {
        var options = new ActionCaptureOptions { FillDebounceMs = 5000 };
        await using var engine = new ActionCaptureEngine(options);
        await CreatePageAndStartEngineAsync(engine);

        engine.ProcessDomEvent(
            """{"type":"input","timestamp":1710000000000,"x":10,"y":20,"value":"hello","pageUrl":"https://example.com"}""");
        engine.ProcessDomEvent(
            """{"type":"blur","timestamp":1710000000100,"pageUrl":"https://example.com"}""");

        // Blur flushes immediately; no need to wait or stop
        var fill = engine.CapturedActions.OfType<FillAction>().FirstOrDefault();
        Assert.IsNotNull(fill, "Blur should have flushed the pending fill");
        Assert.AreEqual("hello", fill.Value);

        await engine.StopAsync();
    }

    // ---- CDP event tests (synchronous event dispatch path, no Task.Run) ----

    [TestMethod]
    public async Task CdpFrameNavigated_EmitsNavigationAction()
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
