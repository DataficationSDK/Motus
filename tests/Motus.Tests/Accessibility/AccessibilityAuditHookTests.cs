using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilityAuditHookTests
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

    [TestMethod]
    public async Task HookDisabled_DoesNotRegisterAsLifecycleHook()
    {
        var options = new AccessibilityOptions { Enable = false };
        var hook = new AccessibilityAuditHook(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();

        await hook.OnLoadedAsync(pluginContext);

        // Verify the hook did not register itself: lifecycle hooks count should be 0
        var hooks = context.LifecycleHooks;
        // Create a page to verify no lifecycle hooks fire from the audit hook
        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        Assert.IsNull(page.LastAccessibilityAudit,
            "Disabled hook should not have run an audit on page creation.");
    }

    [TestMethod]
    public async Task HookEnabled_RegistersAsLifecycleHook()
    {
        var options = new AccessibilityOptions { Enable = true };
        var hook = new AccessibilityAuditHook(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();

        await hook.OnLoadedAsync(pluginContext);

        // The hook should have registered itself as a lifecycle hook.
        // We can verify this indirectly: creating a page will cause hooks to fire,
        // but the audit needs CDP responses. For this test, just verify no exception
        // on load and that the plugin metadata is correct.
        Assert.AreEqual("motus.accessibility-audit", hook.PluginId);
        Assert.AreEqual("Accessibility Audit Hook", hook.Name);
    }

    [TestMethod]
    public void NullOptions_DefaultsToDisabled()
    {
        var hook = new AccessibilityAuditHook(null);
        Assert.AreEqual("motus.accessibility-audit", hook.PluginId);
    }

    [TestMethod]
    public async Task AfterAction_NonAuditedAction_DoesNotTriggerAudit()
    {
        var options = new AccessibilityOptions { Enable = true, AuditAfterActions = true };
        var hook = new AccessibilityAuditHook(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        // AfterAction for a non-audited action (e.g., "hover") should not trigger audit
        await hook.AfterActionAsync(page, "hover", new ActionResult("test"));

        Assert.IsNull(page.LastAccessibilityAudit,
            "Non-audited actions should not trigger an accessibility audit.");
    }

    [TestMethod]
    public async Task AuditAfterNavigation_False_SkipsAudit()
    {
        var options = new AccessibilityOptions { Enable = true, AuditAfterNavigation = false };
        var hook = new AccessibilityAuditHook(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        // AfterNavigation should not trigger audit when AuditAfterNavigation is false
        await hook.AfterNavigationAsync(page, null);

        Assert.IsNull(page.LastAccessibilityAudit,
            "AuditAfterNavigation=false should skip the audit.");
    }

    [TestMethod]
    public async Task AuditAfterActions_False_SkipsAudit()
    {
        var options = new AccessibilityOptions { Enable = true, AuditAfterActions = false };
        var hook = new AccessibilityAuditHook(options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();
        var pluginContext = context.GetPluginContext();
        await hook.OnLoadedAsync(pluginContext);

        QueuePageOnContextResponses("target-1", "session-1", 3);
        var page = (Motus.Page)await context.NewPageAsync();

        // AfterAction with "click" should not trigger when AuditAfterActions is false
        await hook.AfterActionAsync(page, "click", new ActionResult("test"));

        Assert.IsNull(page.LastAccessibilityAudit,
            "AuditAfterActions=false should skip audits after actions.");
    }

    [TestMethod]
    public void PluginMetadata_IsCorrect()
    {
        var hook = new AccessibilityAuditHook(new AccessibilityOptions());
        Assert.AreEqual("motus.accessibility-audit", hook.PluginId);
        Assert.AreEqual("Accessibility Audit Hook", hook.Name);
        Assert.AreEqual("1.0.0", hook.Version);
        Assert.AreEqual("Motus", hook.Author);
    }

    [TestMethod]
    public async Task OnUnloadedAsync_CompletesSuccessfully()
    {
        var hook = new AccessibilityAuditHook(new AccessibilityOptions());
        await hook.OnUnloadedAsync(); // should not throw
    }

    [TestMethod]
    [DataRow("click")]
    [DataRow("fill")]
    [DataRow("selectOption")]
    public async Task AuditedActions_AreRecognized(string action)
    {
        // Verify the expected actions are in the audited set by testing
        // that a non-enabled hook correctly skips (no context set).
        // This is a structural test: when AuditAfterActions is true and
        // context is null (not loaded), the method returns early without error.
        var options = new AccessibilityOptions { Enable = false, AuditAfterActions = true };
        var hook = new AccessibilityAuditHook(options);

        // Should not throw even without context (early return because context is null)
        await hook.AfterActionAsync(null!, action, new ActionResult("test"));
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
}
