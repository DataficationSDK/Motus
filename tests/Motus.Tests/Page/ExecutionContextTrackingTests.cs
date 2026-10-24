using System.Collections.Concurrent;
using System.Text.Json;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

/// <summary>
/// Which execution context the page sends a frame's evaluations to, as the browser makes and tears
/// down contexts around it.
/// </summary>
/// <remarks>
/// A navigation destroys the old document's main world and creates the new one's, and the load
/// event that ends the navigation travels separately from both. An evaluation that starts the
/// moment a navigation completes must neither name the old document's context, which the browser
/// rejects, nor leave the context out for a frame other than the main one, which runs the script in
/// the main frame instead.
/// </remarks>
[TestClass]
public class ExecutionContextTrackingTests
{
    private MethodAwareFakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    // Every Runtime.evaluate the page sent, with the session it went to and the context it named.
    private readonly ConcurrentQueue<(string? SessionId, int? ContextId)> _evaluations = new();

    // Contexts the fake browser refuses as already torn down.
    private readonly ConcurrentDictionary<int, bool> _deadContexts = new();

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new MethodAwareFakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        _socket.Respond(
            "Browser.getVersion",
            """{"result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");

        await _browser.InitializeAsync(CancellationToken.None);

        _socket.Respond("Target.createBrowserContext", """{"result": {"browserContextId": "ctx-1"}}""");
        _socket.Respond("Target.createTarget", """{"result": {"targetId": "target-1"}}""");
        _socket.Respond("Target.attachToTarget", """{"result": {"sessionId": "session-1"}}""");
        _socket.Respond("Page.getFrameTree", """
            {
                "sessionId": "session-1",
                "result": {
                    "frameTree": {
                        "frame": { "id": "frame-main", "loaderId": "loader-1", "name": "", "url": "about:blank" }
                    }
                }
            }
            """);

        _socket.SetHandler(envelope =>
        {
            var method = envelope.GetProperty("method").GetString();
            if (method != "Runtime.evaluate")
                throw new InvalidOperationException($"Unexpected {method}");

            var id = envelope.GetProperty("id").GetInt32();
            var sessionId = envelope.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
            int? contextId = envelope.GetProperty("params").TryGetProperty("contextId", out var c)
                ? c.GetInt32()
                : null;

            _evaluations.Enqueue((sessionId, contextId));

            if (contextId is { } named && _deadContexts.ContainsKey(named))
            {
                return $$"""
                    {
                        "id": {{id}},
                        "sessionId": "{{sessionId}}",
                        "error": { "code": -32000, "message": "Cannot find context with specified id" }
                    }
                    """;
            }

            return $$"""
                {
                    "id": {{id}},
                    "sessionId": "{{sessionId}}",
                    "result": { "result": { "type": "number", "value": 2 } }
                }
                """;
        });
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task ContextDestroyed_IsNoLongerNamed()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(7, "frame-main");
        await Settle();

        await page.EvaluateAsync<int>("1 + 1");
        Assert.AreEqual(7, LastEvaluation().ContextId);

        ContextDestroyed(7);
        await Settle();

        await page.EvaluateAsync<int>("1 + 1");
        Assert.IsNull(
            LastEvaluation().ContextId,
            "with its context gone, the main frame falls back to the session default, which cannot be stale");
    }

    [TestMethod]
    public async Task ContextsCleared_IsNoLongerNamed()
    {
        var page = await _browser.NewPageAsync();
        AttachFrame("frame-child", "frame-main");
        ContextCreated(7, "frame-main");
        ContextCreated(8, "frame-child");
        await Settle();

        ContextsCleared();
        await Settle();

        await page.EvaluateAsync<int>("1 + 1");
        Assert.IsNull(LastEvaluation().ContextId, "the main frame's context was cleared with the rest");

        var child = Frame(page, "frame-child");
        var pending = child.EvaluateAsync<int>("1 + 1");
        await Settle();
        Assert.IsFalse(pending.IsCompleted, "the child's context was cleared too, so it waits for its next");

        ContextCreated(9, "frame-child");
        Assert.AreEqual(2, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(9, LastEvaluation().ContextId);
    }

    [TestMethod]
    public async Task IsolatedWorld_DoesNotReplaceTheMainWorld()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(7, "frame-main");
        ContextCreated(12, "frame-main", isDefault: false);
        await Settle();

        await page.EvaluateAsync<int>("1 + 1");

        Assert.AreEqual(7, LastEvaluation().ContextId, "an isolated world is not the frame's context");
    }

    [TestMethod]
    public async Task DestroyedThenCreated_OnTheSameSession_KeepsTheNewContext()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(7, "frame-main");
        await Settle();

        // The order a navigation reports them in. Read on separate loops, the teardown could be
        // handled after the replacement and leave the frame with nothing.
        ContextDestroyed(7);
        ContextCreated(8, "frame-main");
        await Settle();

        await page.EvaluateAsync<int>("1 + 1");

        Assert.AreEqual(8, LastEvaluation().ContextId);
    }

    [TestMethod]
    public async Task ChildFrame_WithNoContextYet_WaitsForItsOwnRatherThanRunningInTheMainFrame()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(7, "frame-main");
        AttachFrame("frame-child", "frame-main");
        await Settle();

        var pending = Frame(page, "frame-child").EvaluateAsync<int>("1 + 1");
        await Settle();

        Assert.IsFalse(pending.IsCompleted, "nothing is sent until the frame has a context of its own");
        Assert.AreEqual(0, _evaluations.Count);

        ContextCreated(11, "frame-child");

        Assert.AreEqual(2, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(11, LastEvaluation().ContextId);
    }

    [TestMethod]
    public async Task StaleContext_IsRetriedOnceAgainstTheNextOne()
    {
        var page = await _browser.NewPageAsync();
        AttachFrame("frame-child", "frame-main");
        ContextCreated(11, "frame-child");
        await Settle();

        // The browser has torn the context down but the page has not heard yet, which is the window
        // between a navigation's load event and its context events.
        _deadContexts[11] = true;

        var pending = Frame(page, "frame-child").EvaluateAsync<int>("1 + 1");
        await Settle();

        Assert.IsFalse(pending.IsCompleted, "after the refusal, the retry waits for the frame's next context");
        Assert.AreEqual(1, _evaluations.Count);

        ContextCreated(12, "frame-child");

        Assert.AreEqual(2, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
        CollectionAssert.AreEqual(
            new int?[] { 11, 12 },
            _evaluations.Select(e => e.ContextId).ToArray(),
            "one refused attempt against the dead context, then one against the new one");
    }

    [TestMethod]
    public async Task StaleContext_OnTheMainFrame_IsRetriedOnce()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(7, "frame-main");
        await Settle();
        _deadContexts[7] = true;

        Assert.AreEqual(2, await page.EvaluateAsync<int>("1 + 1"));

        var sent = _evaluations.Select(e => e.ContextId).ToArray();
        Assert.AreEqual(2, sent.Length, "exactly one retry");
        Assert.AreEqual(7, sent[0]);
        Assert.IsNull(sent[1], "the retry goes to the main frame's current default context");
    }

    [TestMethod]
    public async Task StaleContext_RefusedTwice_IsNotRetriedAgain()
    {
        var page = await _browser.NewPageAsync();
        AttachFrame("frame-child", "frame-main");
        ContextCreated(11, "frame-child");
        await Settle();
        _deadContexts[11] = true;
        _deadContexts[12] = true;

        var pending = Frame(page, "frame-child").EvaluateAsync<int>("1 + 1");
        await Settle();
        ContextCreated(12, "frame-child");

        var ex = await Assert.ThrowsExceptionAsync<MotusProtocolException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        StringAssert.Contains(ex.Message, "Cannot find context");
        Assert.AreEqual(2, _evaluations.Count, "one retry, not a loop");
    }

    [TestMethod]
    public async Task DetachedFrame_FailsAtOnce()
    {
        var page = await _browser.NewPageAsync();
        AttachFrame("frame-child", "frame-main");
        await Settle();
        var child = Frame(page, "frame-child");

        DetachFrame("frame-child");
        await Settle();

        await Assert.ThrowsExceptionAsync<MotusTargetClosedException>(
            () => child.EvaluateAsync<int>("1 + 1").WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, _evaluations.Count, "nothing is sent for a frame that has gone");
    }

    [TestMethod]
    public async Task FrameDetachedWhileWaiting_FailsTheWaiter()
    {
        var page = await _browser.NewPageAsync();
        AttachFrame("frame-child", "frame-main");
        await Settle();

        var pending = Frame(page, "frame-child").EvaluateAsync<int>("1 + 1");
        await Settle();
        Assert.IsFalse(pending.IsCompleted);

        DetachFrame("frame-child");

        await Assert.ThrowsExceptionAsync<MotusTargetClosedException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual(0, _evaluations.Count);
    }

    [TestMethod]
    public async Task PageClosedWhileWaiting_FailsTheWaiter()
    {
        var page = await _browser.NewPageAsync();
        AttachFrame("frame-child", "frame-main");
        await Settle();

        var pending = Frame(page, "frame-child").EvaluateAsync<int>("1 + 1");
        await Settle();
        Assert.IsFalse(pending.IsCompleted);

        await page.DisposeAsync();

        var ex = await Assert.ThrowsExceptionAsync<MotusTargetClosedException>(
            () => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.AreEqual("page", ex.TargetType);
    }

    [TestMethod]
    public async Task ContextEventsFromOneSession_LeaveAnotherSessionsContextAlone()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(5, "frame-main");
        await Settle();

        AdoptOutOfProcessFrame();
        await Settle(250);

        // Context ids are numbered per renderer, so the frame's own process can hand out the same
        // number the page's process already used.
        ContextCreated(5, "frame-oop", sessionId: "session-2");
        await Settle();

        // The page's process reports its own context 5 gone, and then clears the lot.
        ContextDestroyed(5);
        ContextsCleared();
        await Settle();

        var oop = Frame(page, "frame-oop");
        Assert.AreEqual(2, await oop.EvaluateAsync<int>("1 + 1").WaitAsync(TimeSpan.FromSeconds(5)));

        var last = LastEvaluation();
        Assert.AreEqual("session-2", last.SessionId);
        Assert.AreEqual(5, last.ContextId, "the frame's own process still holds its context");
    }

    [TestMethod]
    public async Task FrameReturningToThePageProcess_KeepsTheContextItReportedThere()
    {
        var page = await _browser.NewPageAsync();
        ContextCreated(5, "frame-main");
        AdoptOutOfProcessFrame();
        await Settle(250);
        ContextCreated(5, "frame-oop", sessionId: "session-2");
        await Settle();

        // The frame navigates back to its parent's origin. The page's process reports the new
        // document's context, and the frame's own target goes away. The two travel separately, so
        // the context can be recorded before the target's departure is handled, and the departure
        // must not take the new context with it.
        ContextCreated(20, "frame-oop");
        await Settle();
        TargetDetached("session-2");
        await Settle();

        var oop = Frame(page, "frame-oop");
        Assert.AreEqual(2, await oop.EvaluateAsync<int>("1 + 1").WaitAsync(TimeSpan.FromSeconds(5)));

        var last = LastEvaluation();
        Assert.AreEqual("session-1", last.SessionId);
        Assert.AreEqual(20, last.ContextId, "the context the page's process reported for the frame");
    }

    private (string? SessionId, int? ContextId) LastEvaluation()
    {
        Assert.IsTrue(_evaluations.Count > 0, "no Runtime.evaluate was sent");
        return _evaluations.Last();
    }

    private static IFrame Frame(IPage page, string frameId)
        => page.Frames.Single(f => ((global::Motus.Frame)f).Id == frameId);

    private static Task Settle(int ms = 100) => Task.Delay(ms);

    private void ContextCreated(int id, string frameId, bool isDefault = true, string sessionId = "session-1")
        => _socket.Enqueue($$"""
            {
                "method": "Runtime.executionContextCreated",
                "sessionId": "{{sessionId}}",
                "params": {
                    "context": {
                        "id": {{id}},
                        "origin": "://",
                        "name": "",
                        "auxData": { "frameId": "{{frameId}}", "isDefault": {{(isDefault ? "true" : "false")}} }
                    }
                }
            }
            """);

    private void ContextDestroyed(int id, string sessionId = "session-1")
        => _socket.Enqueue($$"""
            {
                "method": "Runtime.executionContextDestroyed",
                "sessionId": "{{sessionId}}",
                "params": { "executionContextId": {{id}} }
            }
            """);

    private void ContextsCleared(string sessionId = "session-1")
        => _socket.Enqueue($$"""
            {
                "method": "Runtime.executionContextsCleared",
                "sessionId": "{{sessionId}}",
                "params": {}
            }
            """);

    private void AttachFrame(string frameId, string parent)
        => _socket.Enqueue($$"""
            {
                "method": "Page.frameAttached",
                "sessionId": "session-1",
                "params": { "frameId": "{{frameId}}", "parentFrameId": "{{parent}}" }
            }
            """);

    private void DetachFrame(string frameId)
        => _socket.Enqueue($$"""
            {
                "method": "Page.frameDetached",
                "sessionId": "session-1",
                "params": { "frameId": "{{frameId}}" }
            }
            """);

    private void TargetDetached(string sessionId)
        => _socket.Enqueue($$"""
            {
                "method": "Target.detachedFromTarget",
                "sessionId": "session-1",
                "params": { "sessionId": "{{sessionId}}" }
            }
            """);

    /// <summary>
    /// Has the page adopt a frame that renders in a process of its own, reached over session-2.
    /// </summary>
    private void AdoptOutOfProcessFrame()
    {
        _socket.Respond("Page.getFrameTree", """
            {
                "sessionId": "session-2",
                "result": {
                    "frameTree": {
                        "frame": {
                            "id": "frame-oop",
                            "parentId": "frame-main",
                            "loaderId": "loader-2",
                            "name": "",
                            "url": "http://b.test/child.html"
                        }
                    }
                }
            }
            """);

        _socket.Enqueue("""
            {
                "method": "Target.attachedToTarget",
                "sessionId": "session-1",
                "params": {
                    "sessionId": "session-2",
                    "waitingForDebugger": false,
                    "targetInfo": {
                        "targetId": "frame-oop",
                        "type": "iframe",
                        "title": "child",
                        "url": "http://b.test/child.html",
                        "attached": true
                    }
                }
            }
            """);
    }
}
