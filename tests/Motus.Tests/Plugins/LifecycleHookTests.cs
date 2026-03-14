using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Plugins;

/// <summary>
/// Records all lifecycle hook method calls for verification in tests.
/// </summary>
internal sealed class RecordingLifecycleHook : ILifecycleHook
{
    internal List<string> Calls { get; } = [];

    public Task BeforeNavigationAsync(IPage page, string url)
    {
        Calls.Add($"BeforeNavigation:{url}");
        return Task.CompletedTask;
    }

    public Task AfterNavigationAsync(IPage page, IResponse? response)
    {
        Calls.Add("AfterNavigation");
        return Task.CompletedTask;
    }

    public Task BeforeActionAsync(IPage page, string action)
    {
        Calls.Add($"BeforeAction:{action}");
        return Task.CompletedTask;
    }

    public Task AfterActionAsync(IPage page, string action, ActionResult result)
    {
        var status = result.Error is null ? "ok" : "error";
        Calls.Add($"AfterAction:{action}:{status}");
        return Task.CompletedTask;
    }

    public Task OnPageCreatedAsync(IPage page)
    {
        Calls.Add("OnPageCreated");
        return Task.CompletedTask;
    }

    public Task OnPageClosedAsync(IPage page)
    {
        Calls.Add("OnPageClosed");
        return Task.CompletedTask;
    }

    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message)
    {
        Calls.Add($"OnConsoleMessage:{message.Type}");
        return Task.CompletedTask;
    }

    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error)
    {
        Calls.Add($"OnPageError:{error.Message}");
        return Task.CompletedTask;
    }
}

/// <summary>
/// A hook that throws on every method, used to verify exception isolation.
/// </summary>
internal sealed class ThrowingLifecycleHook : ILifecycleHook
{
    public Task BeforeNavigationAsync(IPage page, string url) => throw new InvalidOperationException("boom");
    public Task AfterNavigationAsync(IPage page, IResponse? response) => throw new InvalidOperationException("boom");
    public Task BeforeActionAsync(IPage page, string action) => throw new InvalidOperationException("boom");
    public Task AfterActionAsync(IPage page, string action, ActionResult result) => throw new InvalidOperationException("boom");
    public Task OnPageCreatedAsync(IPage page) => throw new InvalidOperationException("boom");
    public Task OnPageClosedAsync(IPage page) => throw new InvalidOperationException("boom");
    public Task OnConsoleMessageAsync(IPage page, ConsoleMessageEventArgs message) => throw new InvalidOperationException("boom");
    public Task OnPageErrorAsync(IPage page, PageErrorEventArgs error) => throw new InvalidOperationException("boom");
}

[TestClass]
public class LifecycleHookTests
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

    private async Task<(IBrowserContext context, RecordingLifecycleHook hook)> CreateContextWithHookAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = await _browser.NewContextAsync();

        var hook = new RecordingLifecycleHook();
        var pluginContext = ((Motus.BrowserContext)context).GetPluginContext();
        pluginContext.RegisterLifecycleHook(hook);

        return (context, hook);
    }

    private void QueueClickActionabilityAndMouseResponses(int startId)
    {
        var id = startId;
        // resolve (strategy: evaluate + getProperties = 2 calls)
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-btn-1""}}}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": [{{""name"": ""0"", ""value"": {{""type"": ""object"", ""objectId"": ""btn-1""}}}}, {{""name"": ""length"", ""value"": {{""type"": ""number"", ""value"": 1}}}}]}}}}");
        // visible
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // enabled
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // stable
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // receives-events: pure JS elementFromPoint check
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""boolean"", ""value"": true}}}}}}");
        // bounding box for click
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""value"": {{""x"": 10, ""y"": 10, ""width"": 50, ""height"": 30}}}}}}}}");
        // mouse events
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
        _socket.QueueResponse($@"{{""id"": {id++}, ""sessionId"": ""session-1"", ""result"": {{}}}}");
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

    [TestMethod]
    public async Task OnPageCreated_FiredOnNewPageAsync()
    {
        var (context, hook) = await CreateContextWithHookAsync();

        QueuePageOnContextResponses("target-1", "session-1", 3);
        await context.NewPageAsync();

        Assert.IsTrue(hook.Calls.Contains("OnPageCreated"),
            "OnPageCreated hook should fire when NewPageAsync is called.");
    }

    [TestMethod]
    public async Task OnPageClosed_FiredOnCloseAsync()
    {
        var (context, hook) = await CreateContextWithHookAsync();

        QueuePageOnContextResponses("target-1", "session-1", 3);
        await context.NewPageAsync();

        _socket.QueueResponse("""{"id": 9, "result": {}}""");
        await context.CloseAsync();

        Assert.IsTrue(hook.Calls.Contains("OnPageClosed"),
            "OnPageClosed hook should fire when CloseAsync is called.");
    }

    [TestMethod]
    public async Task BeforeAndAfterAction_FiredOnClick()
    {
        var (context, hook) = await CreateContextWithHookAsync();

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = await context.NewPageAsync();
        var locator = page.Locator("#btn");

        // Queue full actionability + click responses
        QueueClickActionabilityAndMouseResponses(9);

        await locator.ClickAsync();

        Assert.IsTrue(hook.Calls.Contains("BeforeAction:click"),
            "BeforeAction hook should fire before click.");
        Assert.IsTrue(hook.Calls.Contains("AfterAction:click:ok"),
            "AfterAction hook should fire after successful click.");

        // BeforeAction should come before AfterAction
        var beforeIdx = hook.Calls.IndexOf("BeforeAction:click");
        var afterIdx = hook.Calls.IndexOf("AfterAction:click:ok");
        Assert.IsTrue(beforeIdx < afterIdx, "BeforeAction should precede AfterAction.");
    }

    [TestMethod]
    public async Task AfterAction_IncludesErrorOnFailure()
    {
        var (context, hook) = await CreateContextWithHookAsync();

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = await context.NewPageAsync();
        var locator = page.Locator("#missing");

        // Queue empty strategy resolve responses (evaluate + getProperties) for retries until timeout
        for (int i = 0; i < 40; i += 2)
        {
            _socket.QueueResponse($@"{{""id"": {9 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": {{""type"": ""object"", ""objectId"": ""arr-empty-{i}""}}}}}}");
            _socket.QueueResponse($@"{{""id"": {10 + i}, ""sessionId"": ""session-1"", ""result"": {{""result"": []}}}}");
        }

        try
        {
            await locator.ClickAsync(timeout: 200);
        }
        catch (ActionTimeoutException)
        {
            // Expected
        }

        Assert.IsTrue(hook.Calls.Contains("BeforeAction:click"));
        Assert.IsTrue(hook.Calls.Any(c => c.StartsWith("AfterAction:click:error")),
            "AfterAction should include error status on failure.");
    }

    [TestMethod]
    public async Task HookException_DoesNotPropagateToAction()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-2"}}""");
        var context = await _browser.NewContextAsync();

        // Register a throwing hook
        var pluginContext = ((Motus.BrowserContext)context).GetPluginContext();
        pluginContext.RegisterLifecycleHook(new ThrowingLifecycleHook());

        QueuePageOnContextResponses("target-2", "session-2", 3);
        // The hook throws on OnPageCreated, but it should not propagate
        var page = await context.NewPageAsync();

        Assert.IsNotNull(page, "Page creation should succeed even when hook throws.");
    }

    [TestMethod]
    public async Task PluginContext_RegisterWaitCondition_IsRetrievable()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-3"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();

        var pluginCtx = context.GetPluginContext();
        var condition = new TestWaitCondition("my-condition");
        pluginCtx.RegisterWaitCondition(condition);

        var retrieved = context.GetWaitCondition("my-condition");
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("my-condition", retrieved.ConditionName);
    }

    [TestMethod]
    public async Task PluginContext_RegisterSelectorStrategy_IsRetrievable()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-4"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();

        var pluginCtx = context.GetPluginContext();
        var strategy = new TestSelectorStrategy("custom-attr");
        pluginCtx.RegisterSelectorStrategy(strategy);

        Assert.IsTrue(context.SelectorStrategies.TryGetStrategy("custom-attr", out var retrieved));
        Assert.IsNotNull(retrieved);
        Assert.AreEqual("custom-attr", retrieved!.StrategyName);
    }

    private sealed class TestSelectorStrategy : ISelectorStrategy
    {
        public TestSelectorStrategy(string name) => StrategyName = name;
        public string StrategyName { get; }
        public int Priority => 50;
        public Task<IReadOnlyList<IElementHandle>> ResolveAsync(
            string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IElementHandle>>([]);
        public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private sealed class TestWaitCondition : IWaitCondition
    {
        public TestWaitCondition(string name) => ConditionName = name;
        public string ConditionName { get; }
        public Task<bool> EvaluateAsync(IPage page, WaitConditionOptions? options = null)
            => Task.FromResult(true);
    }
}
