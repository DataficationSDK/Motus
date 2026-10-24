using System.Diagnostics;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class ActionabilityTests
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
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    /// <summary>
    /// Queues 2 CDP responses for strategy-based element resolution:
    /// 1. Runtime.evaluate (querySelectorAll returning array objectId)
    /// 2. Runtime.getProperties (returning element descriptors)
    /// </summary>
    private void QueueStrategyResolve(ref int id, string objectId)
    {
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-{objectId}""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""{objectId}""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
    }

    /// <summary>
    /// Queues the standard actionability responses for a full click:
    /// visible (1) + enabled (1) + scrollIntoView (1) + stable (1) + receivesEvents (1) + boundingBox (1) + mouse (3)
    /// </summary>
    private void QueueClickResponses(ref int id, double x, double y, double w, double h)
    {
        // visible -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // scroll into view -> false (already in view, nothing scrolled)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": false}}}}}}");
        // stable -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events -> true (pure JS elementFromPoint check)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // GetBoundingBoxOrThrow
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": {x}, ""y"": {y}, ""width"": {w}, ""height"": {h}}}}}}}}}");
        // mouse events (move, down, up)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
    }

    [TestMethod]
    public async Task VisibilityCheck_PassesWhenVisible()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#btn");

        var id = 9;
        QueueStrategyResolve(ref id, "btn-1");
        QueueClickResponses(ref id, 10, 20, 100, 50);

        await locator.ClickAsync();
    }

    [TestMethod]
    public async Task EnabledCheck_PassesWhenEnabled()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("input#name");

        var id = 9;
        // resolve (strategy: 2 calls)
        QueueStrategyResolve(ref id, "input-1");
        // visible check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // editable check -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // EvalOnElementVoidAsync for fill action
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""undefined""}}}}}}");

        await locator.FillAsync("test value");
    }

    [TestMethod]
    public async Task TimeoutThrows_WhenElementNeverAppears()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#never-exists");

        // Strategy resolve returns empty arrays repeatedly
        for (int i = 0; i < 40; i += 2)
        {
            _socket.QueueResponse($@"{{""id"": {9 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-empty-{i}""}}}}}}");
            _socket.QueueResponse($@"{{""id"": {10 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": []}}}}");
        }

        await Assert.ThrowsExceptionAsync<ActionTimeoutException>(
            () => locator.ClickAsync(timeout: 200));
    }

    [TestMethod]
    public async Task StabilityCheck_SendsRafJs()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#anim");

        var id = 9;
        QueueStrategyResolve(ref id, "anim-1");
        QueueClickResponses(ref id, 0, 0, 50, 50);

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("setTimeout")),
            "Stability check should include setTimeout in the JS function.");
    }

    [TestMethod]
    public async Task ReceivesEventsCheck_UsesElementFromPoint()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#overlay-target");

        var id = 9;
        QueueStrategyResolve(ref id, "ot-1");
        QueueClickResponses(ref id, 50, 50, 100, 40);

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        // ReceivesEvents now uses a pure JS elementFromPoint check instead of DOM domain calls
        Assert.IsTrue(allSent.Any(s => s.Contains("elementFromPoint")),
            "Receives-events check should use document.elementFromPoint.");
    }

    [TestMethod]
    public async Task HoverAsync_UsesVisibleStableReceivesEventsFlags()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#hoverable");

        var id = 9;
        QueueStrategyResolve(ref id, "hov-1");
        // visible -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // scroll into view -> false (no enabled check for hover)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": false}}}}}}");
        // stable -> true
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events -> true (pure JS elementFromPoint check)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // bounding box for hover
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 0, ""y"": 0, ""width"": 60, ""height"": 30}}}}}}}}");
        // mouseMoved
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");

        await locator.HoverAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        Assert.IsTrue(allSent.Any(s => s.Contains("mouseMoved")));
    }

    [TestMethod]
    public async Task ClickAsync_ScrollsIntoViewBeforeTheStabilityAndHitTests()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#below");

        var id = 9;
        QueueStrategyResolve(ref id, "below-1");
        QueueClickResponses(ref id, 10, 20, 100, 50);

        await locator.ClickAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();

        var scroll = allSent.FindIndex(s => s.Contains("scrollIntoView"));
        var stable = allSent.FindIndex(s => s.Contains("setTimeout"));
        var hitTest = allSent.FindIndex(s => s.Contains("elementFromPoint"));

        Assert.IsTrue(scroll >= 0, "The click never asked for the element to be scrolled into view.");
        Assert.IsTrue(scroll < stable && scroll < hitTest,
            "The element has to be scrolled into view before it is measured and hit-tested.");
    }

    // A check reads the state first, then runs the pointer checks a second time before clicking.
    private void QueueCheckResponses(ref int id, string objectId, string stateBefore, string stateAfter)
    {
        QueueStrategyResolve(ref id, objectId);
        // visible, enabled
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // checked state before the click
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""{stateBefore}""}}}}}}");
        QueueStrategyResolve(ref id, objectId);
        QueueClickResponses(ref id, 0, 0, 20, 20);
        // checked state after the click
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""{stateAfter}""}}}}}}");
    }

    [TestMethod]
    public async Task CheckAsync_WhenTheClickChangesTheState_Succeeds()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueCheckResponses(ref id, "cb-1", "checkbox:false", "checkbox:true");

        await page.Locator("#cb").CheckAsync();

        Assert.IsTrue(Enumerable.Range(0, _socket.SentMessages.Count)
            .Any(i => _socket.GetSentJson(i).Contains("mousePressed")));
    }

    [TestMethod]
    public async Task CheckAsync_WhenTheClickLeavesTheStateUnchanged_Throws()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueCheckResponses(ref id, "cb-1", "checkbox:false", "checkbox:false");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => page.Locator("#cb").CheckAsync());

        StringAssert.Contains(ex.Message, "#cb");
        StringAssert.Contains(ex.Message, "still unchecked");
    }

    [TestMethod]
    public async Task CheckAsync_WhenTheClickTakesTheDocumentAway_Succeeds()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueStrategyResolve(ref id, "cb-1");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""checkbox:false""}}}}}}");
        QueueStrategyResolve(ref id, "cb-1");
        QueueClickResponses(ref id, 0, 0, 20, 20);
        // The box submitted its form, so the element it was read through is gone.
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""error"": {{""code"": -32000, ""message"": ""Cannot find context with specified id""}}}}");

        await page.Locator("#cb").CheckAsync();
    }

    [TestMethod]
    public async Task UncheckAsync_WhenTheClickLeavesTheStateUnchanged_Throws()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueCheckResponses(ref id, "cb-1", "checkbox:true", "checkbox:true");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => page.Locator("#cb").UncheckAsync());

        StringAssert.Contains(ex.Message, "still checked");
    }

    [TestMethod]
    public async Task CheckAsync_WhenAlreadyChecked_DoesNotClick()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueStrategyResolve(ref id, "cb-1");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""checkbox:true""}}}}}}");

        await page.Locator("#cb").CheckAsync();

        var allSent = Enumerable.Range(0, _socket.SentMessages.Count)
            .Select(i => _socket.GetSentJson(i))
            .ToList();
        Assert.IsFalse(allSent.Any(s => s.Contains("mousePressed")), "An already checked box was clicked.");
        Assert.IsFalse(allSent.Any(s => s.Contains("scrollIntoView")), "An already checked box was scrolled to.");
    }

    [TestMethod]
    public async Task CheckAsync_OnAnElementWithNoCheckedState_Throws()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueStrategyResolve(ref id, "btn-1");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""subtype"": ""null"", ""value"": null}}}}}}");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => page.Locator("#btn").CheckAsync());

        StringAssert.Contains(ex.Message, "not a checkbox");
    }

    [TestMethod]
    public async Task UncheckAsync_OnACheckedRadioButton_ThrowsWithoutClicking()
    {
        var page = await CreatePageAsync();
        var id = 9;
        QueueStrategyResolve(ref id, "r-1");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""string"", ""value"": ""radio:true""}}}}}}");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => page.Locator("#r").UncheckAsync());

        StringAssert.Contains(ex.Message, "radio button");
        Assert.IsFalse(Enumerable.Range(0, _socket.SentMessages.Count)
            .Any(i => _socket.GetSentJson(i).Contains("mousePressed")));
    }

    [TestMethod]
    public async Task FocusAsync_UsesNoActionabilityFlags()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#focusable");

        var id = 9;
        // resolve (strategy: 2 calls)
        QueueStrategyResolve(ref id, "f-1");
        // focus eval
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""undefined""}}}}}}");

        await locator.FocusAsync();
    }

    // --- the timeout a caller asks for ---

    /// <summary>
    /// Queues enough empty resolutions for an element that never turns up, so the wait ends on the
    /// timeout rather than on an answer.
    /// </summary>
    private void QueueNeverResolves()
    {
        for (int i = 0; i < 40; i += 2)
        {
            _socket.QueueResponse($@"{{""id"": {9 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-empty-{i}""}}}}}}");
            _socket.QueueResponse($@"{{""id"": {10 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": []}}}}");
        }
    }

    /// <summary>
    /// Runs an action that cannot succeed and asserts it gave up on the short timeout it was given
    /// rather than on the thirty second default.
    /// </summary>
    private static async Task AssertGivesUpQuicklyAsync(Func<Task> action)
    {
        var started = Stopwatch.StartNew();
        await Assert.ThrowsExceptionAsync<ActionTimeoutException>(() => action());
        started.Stop();

        Assert.IsTrue(
            started.Elapsed < TimeSpan.FromSeconds(5),
            $"the call waited {started.ElapsedMilliseconds} ms, so the timeout it was given was ignored.");
    }

    [TestMethod]
    public async Task TypeAsync_HonoursTheTimeoutInItsOptions()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#never-exists");
        QueueNeverResolves();

        await AssertGivesUpQuicklyAsync(
            () => locator.TypeAsync("hello", new KeyboardTypeOptions(Timeout: 200)));
    }

    [TestMethod]
    public async Task PressAsync_HonoursTheTimeoutInItsOptions()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#never-exists");
        QueueNeverResolves();

        await AssertGivesUpQuicklyAsync(
            () => locator.PressAsync("Enter", new KeyboardPressOptions(Timeout: 200)));
    }

    [TestMethod]
    public async Task SelectOptionAsync_HonoursTheTimeoutItIsGiven()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#never-exists");
        QueueNeverResolves();

        await AssertGivesUpQuicklyAsync(
            () => locator.SelectOptionAsync(["us"], timeout: 200));
    }

    /// <summary>
    /// A timeout given to <see cref="ILocator.Filter"/> survives onto the locator it returns, the
    /// same way it does when a child locator is built with options.
    /// </summary>
    [TestMethod]
    public async Task Filter_CarriesTheTimeoutItWasGiven()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("#never-exists").Filter(new LocatorOptions { Timeout = 200 });
        QueueNeverResolves();

        await AssertGivesUpQuicklyAsync(() => locator.ClickAsync());
    }
}
