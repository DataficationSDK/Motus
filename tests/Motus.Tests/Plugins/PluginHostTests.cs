using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Plugins;

[TestClass]
public class PluginHostTests
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
    public async Task LoadAsync_CallsOnLoadedForEachPlugin()
    {
        var pluginA = new StubPlugin("a");
        var pluginB = new StubPlugin("b");
        var options = new LaunchOptions { Plugins = [pluginA, pluginB] };

        var host = new PluginHost();
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();

        await host.LoadAsync(options, context);

        Assert.IsTrue(pluginA.Loaded, "PluginA.OnLoadedAsync should have been called.");
        Assert.IsTrue(pluginB.Loaded, "PluginB.OnLoadedAsync should have been called.");
    }

    [TestMethod]
    public async Task UnloadAsync_CallsOnUnloadedInReverseOrder()
    {
        var order = new List<string>();
        var pluginA = new StubPlugin("a", onUnloaded: () => order.Add("a"));
        var pluginB = new StubPlugin("b", onUnloaded: () => order.Add("b"));
        var options = new LaunchOptions { Plugins = [pluginA, pluginB] };

        var host = new PluginHost();
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();

        await host.LoadAsync(options, context);
        await host.UnloadAsync();

        CollectionAssert.AreEqual(new[] { "b", "a" }, order,
            "Plugins should unload in reverse order.");
    }

    [TestMethod]
    public async Task ManualPlugins_TakePrecedence_OverDiscovered()
    {
        var manualPlugin = new StubPlugin("shared-id", name: "Manual");

        // Set up discovery bridge to return a plugin with the same ID
        var originalFactory = PluginDiscovery.Factory;
        try
        {
            PluginDiscovery.Factory = () => [new StubPlugin("shared-id", name: "Discovered")];

            var options = new LaunchOptions { Plugins = [manualPlugin] };
            var host = new PluginHost();
            _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
            var context = (Motus.BrowserContext)await _browser.NewContextAsync();

            await host.LoadAsync(options, context);

            // 1 built-in + 1 manual (discovered duplicate suppressed)
            Assert.AreEqual(2, host.Plugins.Count, "Duplicate PluginId should be deduplicated.");
            Assert.AreEqual("Manual", host.Plugins[1].Name, "Manual plugin should take precedence.");
        }
        finally
        {
            PluginDiscovery.Factory = originalFactory;
        }
    }

    [TestMethod]
    public async Task FailingOnLoaded_DoesNotPreventOtherPlugins()
    {
        var failingPlugin = new StubPlugin("fail", throwOnLoad: true);
        var goodPlugin = new StubPlugin("good");
        var options = new LaunchOptions { Plugins = [failingPlugin, goodPlugin] };

        var host = new PluginHost();
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();

        await host.LoadAsync(options, context);

        Assert.IsTrue(goodPlugin.Loaded, "Good plugin should still load after a failing one.");
        // 1 built-in + 1 good (failing plugin excluded)
        Assert.AreEqual(2, host.Plugins.Count, "Only successfully loaded plugins should be tracked.");
    }

    [TestMethod]
    public async Task FailingOnUnloaded_DoesNotPreventOtherPlugins()
    {
        var order = new List<string>();
        var failingPlugin = new StubPlugin("fail-unload", throwOnUnload: true);
        var goodPlugin = new StubPlugin("good", onUnloaded: () => order.Add("good"));
        var options = new LaunchOptions { Plugins = [goodPlugin, failingPlugin] };

        var host = new PluginHost();
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        var context = (Motus.BrowserContext)await _browser.NewContextAsync();

        await host.LoadAsync(options, context);
        await host.UnloadAsync();

        Assert.IsTrue(order.Contains("good"),
            "Good plugin should unload even when another plugin's unload fails.");
    }

    [TestMethod]
    public async Task Plugin_RegistersLifecycleHook_ViaPluginContext()
    {
        var hook = new RecordingLifecycleHook();
        var plugin = new StubPlugin("hook-plugin", onLoaded: ctx => ctx.RegisterLifecycleHook(hook));
        var options = new LaunchOptions { Plugins = [plugin] };

        // Create browser with plugin options
        var browser = new Motus.Browser(_transport, _registry, process: null,
            tempUserDataDir: null, handleSigint: false, handleSigterm: false, options);

        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-hook"}}""");
        var context = (Motus.BrowserContext)await browser.NewContextAsync();

        // The hook should be registered via the plugin
        Assert.IsNotNull(context.PluginHost);

        // Verify hook fires on page creation
        // Queue responses for NewPageAsync
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");

        await context.NewPageAsync();

        Assert.IsTrue(hook.Calls.Contains("OnPageCreated"),
            "Lifecycle hook registered by plugin should fire on page creation.");
    }

    private sealed class StubPlugin : IPlugin
    {
        private readonly bool _throwOnLoad;
        private readonly bool _throwOnUnload;
        private readonly Action? _onUnloaded;
        private readonly Action<IPluginContext>? _onLoaded;

        public StubPlugin(
            string pluginId,
            string? name = null,
            bool throwOnLoad = false,
            bool throwOnUnload = false,
            Action? onUnloaded = null,
            Action<IPluginContext>? onLoaded = null)
        {
            PluginId = pluginId;
            Name = name ?? pluginId;
            _throwOnLoad = throwOnLoad;
            _throwOnUnload = throwOnUnload;
            _onUnloaded = onUnloaded;
            _onLoaded = onLoaded;
        }

        public string PluginId { get; }
        public string Name { get; }
        public string Version => "1.0.0";
        public string? Author => null;
        public string? Description => null;
        public bool Loaded { get; private set; }

        public Task OnLoadedAsync(IPluginContext context)
        {
            if (_throwOnLoad) throw new InvalidOperationException("Load failed");
            Loaded = true;
            _onLoaded?.Invoke(context);
            return Task.CompletedTask;
        }

        public Task OnUnloadedAsync()
        {
            if (_throwOnUnload) throw new InvalidOperationException("Unload failed");
            _onUnloaded?.Invoke();
            return Task.CompletedTask;
        }
    }
}
