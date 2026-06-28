using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Tests.Tools;

namespace Motus.Mcp.Tests;

/// <summary>
/// Verifies that a dead browser is detected and relaunched on the next browser-touching call,
/// so a transient browser crash recovers inside the session instead of wedging it. These run
/// without a real browser by driving the launch through <see cref="BrowserSessionManager"/>'s
/// internal launch seam.
/// </summary>
[TestClass]
public class BrowserRecoveryTests
{
    private static BrowserSessionManager NewManager(params FakeBrowser[] browsers)
    {
        var queue = new Queue<FakeBrowser>(browsers);
        return new BrowserSessionManager(new McpServerLaunchOptions())
        {
            LaunchOverride = _ => Task.FromResult<IBrowser>(queue.Dequeue()),
        };
    }

    [TestMethod]
    public async Task EnsureBrowserAsync_HealthyBrowser_IsReusedWithoutRelaunch()
    {
        var browser = new FakeBrowser();
        await using var manager = NewManager(browser);

        var first = await manager.EnsureBrowserAsync();
        var second = await manager.EnsureBrowserAsync();

        Assert.AreSame(first, second);
        Assert.AreEqual(1, manager.Generation);
        Assert.IsFalse(browser.DisposeCalled);
    }

    [TestMethod]
    public async Task EnsureBrowserAsync_DeadBrowser_DisposesAndRelaunches()
    {
        var dead = new FakeBrowser();
        var fresh = new FakeBrowser();
        await using var manager = NewManager(dead, fresh);

        var first = await manager.EnsureBrowserAsync();
        await manager.GetOrCreateActiveContextAsync();
        Assert.AreEqual(1, manager.ContextNames.Count);

        // The browser crashes underneath us.
        dead.IsHealthy = false;

        var second = await manager.EnsureBrowserAsync();

        Assert.AreSame(dead, first);
        Assert.AreSame(fresh, second);
        Assert.IsTrue(dead.DisposeCalled, "the dead browser should be disposed");
        Assert.AreEqual(2, manager.Generation, "a relaunch should advance the generation");
        Assert.AreEqual(0, manager.ContextNames.Count, "contexts of the dead browser should be cleared");
        Assert.IsFalse(manager.IsBrowserDead);
    }

    [TestMethod]
    public async Task GetOrCreateActivePage_AfterCrash_ResolvesPageOnRelaunchedBrowser()
    {
        var dead = new FakeBrowser();
        var fresh = new FakeBrowser();
        await using var manager = NewManager(dead, fresh);
        var pages = new ActivePageService(manager);

        var firstPage = await pages.GetOrCreateActivePageAsync();

        // The browser crashes; the cached page keeps reporting IsClosed == false.
        dead.IsHealthy = false;
        Assert.IsFalse(firstPage.IsClosed);

        var secondPage = await pages.GetOrCreateActivePageAsync();

        Assert.AreNotSame(firstPage, secondPage, "a crash should force a fresh page on the new browser");
        Assert.IsFalse(manager.IsBrowserDead);
        Assert.AreSame(fresh, await manager.EnsureBrowserAsync(), "the page should resolve against the relaunched browser");
        Assert.AreEqual(2, manager.Generation);
    }
}

/// <summary>
/// A browser whose health can be toggled, for driving recovery without a real process. Hands out
/// <see cref="RecoverableFakeContext"/> instances and records whether it was disposed.
/// </summary>
internal sealed class FakeBrowser : IBrowser
{
    private readonly List<IBrowserContext> _contexts = [];

    public bool IsConnected { get; set; } = true;
    public bool IsHealthy { get; set; } = true;
    public bool DisposeCalled { get; private set; }
    public string Version => "FakeBrowser/1.0";

    public IReadOnlyList<IBrowserContext> Contexts
    {
        get
        {
            lock (_contexts)
                return _contexts.ToList();
        }
    }

#pragma warning disable CS0067 // part of the interface; recovery is driven by toggling IsHealthy
    public event EventHandler? Disconnected;
#pragma warning restore CS0067

    public Task<IBrowserContext> NewContextAsync(ContextOptions? options = null)
    {
        var context = new RecoverableFakeContext(this);
        lock (_contexts)
            _contexts.Add(context);
        return Task.FromResult<IBrowserContext>(context);
    }

    public async Task<IPage> NewPageAsync(ContextOptions? options = null)
    {
        var context = await NewContextAsync(options).ConfigureAwait(false);
        return await context.NewPageAsync().ConfigureAwait(false);
    }

    public Task CloseAsync()
    {
        IsConnected = false;
        IsHealthy = false;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalled = true;
        IsConnected = false;
        IsHealthy = false;
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A context that hands out fresh fake pages, so the active-page resolution path can run end to
/// end against a fake browser. Members the recovery path does not touch throw.
/// </summary>
internal sealed class RecoverableFakeContext(FakeBrowser browser) : IBrowserContext
{
    private readonly List<IPage> _pages = [];

    public IBrowser Browser => browser;

    public IReadOnlyList<IPage> Pages
    {
        get
        {
            lock (_pages)
                return _pages.ToList();
        }
    }

    public Task<IPage> NewPageAsync()
    {
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        lock (_pages)
            _pages.Add(page);
        return Task.FromResult<IPage>(page);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public Task CloseAsync() => Task.CompletedTask;

#pragma warning disable CS0067 // events are part of the interface but unused in this fake
    public event EventHandler<IPage>? Page;
    public event EventHandler? Close;
#pragma warning restore CS0067

    public ITracing Tracing => throw new NotImplementedException();
    public Task<IReadOnlyList<Cookie>> CookiesAsync(IEnumerable<string>? urls = null) => throw new NotImplementedException();
    public Task AddCookiesAsync(IEnumerable<Cookie> cookies) => throw new NotImplementedException();
    public Task ClearCookiesAsync() => throw new NotImplementedException();
    public Task GrantPermissionsAsync(IEnumerable<string> permissions, string? origin = null) => throw new NotImplementedException();
    public Task ClearPermissionsAsync() => throw new NotImplementedException();
    public Task SetGeolocationAsync(Geolocation? geolocation) => throw new NotImplementedException();
    public Task RouteAsync(string urlPattern, Func<IRoute, Task> handler) => throw new NotImplementedException();
    public Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null) => throw new NotImplementedException();
    public Task SetOfflineAsync(bool offline) => throw new NotImplementedException();
    public Task SetExtraHTTPHeadersAsync(IDictionary<string, string> headers) => throw new NotImplementedException();
    public Task<StorageState> StorageStateAsync(string? path = null) => throw new NotImplementedException();
    public Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback) => throw new NotImplementedException();
    public Task AddInitScriptAsync(string script) => throw new NotImplementedException();
    public IPluginContext GetPluginContext() => throw new NotImplementedException();
}
