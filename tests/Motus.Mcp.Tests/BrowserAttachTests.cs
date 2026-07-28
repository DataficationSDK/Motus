using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests;

/// <summary>
/// Covers what changes when the session drives a browser it did not start: where the default
/// context comes from, what shutdown is allowed to close, and what recovery does when there is no
/// process of ours to start again.
/// </summary>
/// <remarks>
/// Driven through the session manager's connect seam rather than a real endpoint. The distinction
/// being pinned is ownership, which the fake reports directly, so a real browser would add process
/// management without adding coverage. The end-to-end path is covered by
/// <see cref="BrowserAttachIntegrationTests"/>.
/// </remarks>
[TestClass]
public class BrowserAttachTests
{
    private const string Endpoint = "http://127.0.0.1:9222";

    private static BrowserSessionManager Attached(params FakeBrowser[] browsers)
    {
        var queue = new Queue<FakeBrowser>(browsers);
        return new BrowserSessionManager(new McpServerLaunchOptions { Endpoint = Endpoint })
        {
            ConnectOverride = (_, _) => Task.FromResult<IBrowser>(queue.Dequeue()),
        };
    }

    private static BrowserSessionManager Launched(params FakeBrowser[] browsers)
    {
        var queue = new Queue<FakeBrowser>(browsers);
        return new BrowserSessionManager(new McpServerLaunchOptions())
        {
            LaunchOverride = _ => Task.FromResult<IBrowser>(queue.Dequeue()),
        };
    }

    [TestMethod]
    public async Task EnsureBrowserAsync_WithAnEndpoint_ConnectsRatherThanLaunching()
    {
        var connected = new FakeBrowser();
        await using var manager = new BrowserSessionManager(new McpServerLaunchOptions { Endpoint = Endpoint })
        {
            ConnectOverride = (endpoint, _) =>
            {
                Assert.AreEqual(Endpoint, endpoint, "the configured endpoint should reach the connector");
                return Task.FromResult<IBrowser>(connected);
            },
            LaunchOverride = _ => throw new InvalidOperationException(
                "An endpoint is configured, so nothing should be launched."),
        };

        Assert.AreSame(connected, await manager.EnsureBrowserAsync());
        Assert.AreEqual(Endpoint, manager.Endpoint);
        Assert.IsTrue(manager.IsAttached);
    }

    [TestMethod]
    public async Task IsAttached_BeforeAnyBrowser_IsFalseEvenWithAnEndpointConfigured()
    {
        await using var manager = Attached(new FakeBrowser());

        // Ownership is a fact about the browser in hand, and there is not one yet. Reporting
        // attached here would have browser_status describe a connection that has not been made.
        Assert.IsFalse(manager.IsAttached);
        Assert.AreEqual(Endpoint, manager.Endpoint);
    }

    [TestMethod]
    public async Task DefaultContext_AgainstAnAttachedBrowser_AdoptsTheOneAlreadyOpen()
    {
        var browser = new FakeBrowser();
        var existing = browser.SeedExistingContext();
        await using var manager = Attached(browser);

        var context = await manager.GetOrCreateActiveContextAsync();

        Assert.AreSame(existing, context,
            "attaching exists to drive what is already open, so the default context is the one the "
            + "browser already has rather than a fresh one beside it");
        Assert.AreEqual(1, browser.Contexts.Count, "no second context should have been created");
    }

    [TestMethod]
    public async Task DefaultContext_AgainstALaunchedBrowser_IsStillCreated()
    {
        var browser = new FakeBrowser { OwnsProcess = true };
        browser.SeedExistingContext();
        await using var manager = Launched(browser);

        await manager.GetOrCreateActiveContextAsync();

        // A browser we started gets a context configured the way the session asked for, with its
        // viewport and recording options, rather than whatever the process happened to open with.
        Assert.AreEqual(2, browser.Contexts.Count);
    }

    [TestMethod]
    public async Task NamedContext_AgainstAnAttachedBrowser_IsStillCreated()
    {
        var browser = new FakeBrowser();
        var existing = browser.SeedExistingContext();
        await using var manager = Attached(browser);

        var created = await manager.CreateContextAsync("userB");

        Assert.AreNotSame(existing, created,
            "a named context is a request for isolation; handing back the browser's own context "
            + "would not be that");
    }

    [TestMethod]
    public async Task Dispose_LeavesABrowserItDidNotStartRunning()
    {
        var browser = new FakeBrowser();
        var existing = browser.SeedExistingContext();
        var manager = Attached(browser);
        await manager.GetOrCreateActiveContextAsync();

        await manager.DisposeAsync();

        Assert.IsTrue(browser.DisconnectCalled, "an attached browser should be disconnected from");
        Assert.IsFalse(browser.CloseCalled, "an attached browser must never be closed");
        Assert.IsFalse(existing.CloseCalled,
            "the adopted context holds somebody's open tabs and is not this session's to close");
    }

    [TestMethod]
    public async Task Dispose_ClosesABrowserItStarted()
    {
        var browser = new FakeBrowser { OwnsProcess = true };
        var manager = Launched(browser);
        await manager.EnsureBrowserAsync();

        await manager.DisposeAsync();

        Assert.IsTrue(browser.CloseCalled);
        Assert.IsFalse(manager.IsBrowserLaunched);
    }

    [TestMethod]
    public async Task CloseContext_LeavesAnAdoptedContextAlone()
    {
        var browser = new FakeBrowser();
        var existing = browser.SeedExistingContext();
        await using var manager = Attached(browser);
        await manager.GetOrCreateActiveContextAsync();

        await manager.CloseContextAsync(BrowserSessionManager.DefaultContextName);

        Assert.IsFalse(existing.CloseCalled);
        CollectionAssert.DoesNotContain(
            manager.ContextNames.ToList(), BrowserSessionManager.DefaultContextName,
            "the session should still let go of it, even though it does not close it");
    }

    [TestMethod]
    public async Task AttachAsync_ReplacesALaunchedBrowserAndAdvancesTheGeneration()
    {
        var launched = new FakeBrowser { OwnsProcess = true };
        var remote = new FakeBrowser();
        await using var manager = new BrowserSessionManager(new McpServerLaunchOptions())
        {
            LaunchOverride = _ => Task.FromResult<IBrowser>(launched),
            ConnectOverride = (_, _) => Task.FromResult<IBrowser>(remote),
        };

        await manager.EnsureBrowserAsync();
        await manager.GetOrCreateActiveContextAsync();
        Assert.AreEqual(1, manager.Generation);

        var attached = await manager.AttachAsync(Endpoint);

        Assert.AreSame(remote, attached);
        Assert.IsTrue(launched.CloseCalled, "the browser this session started should be closed on the way out");
        Assert.AreEqual(Endpoint, manager.Endpoint);
        Assert.IsTrue(manager.IsAttached);
        Assert.AreEqual(0, manager.ContextNames.Count, "contexts of the previous browser should be dropped");
        Assert.AreEqual(2, manager.Generation,
            "the generation is what makes cached pages resolve again against the new browser");
    }

    [TestMethod]
    public async Task Recovery_WhenAttached_ReconnectsRatherThanLaunching()
    {
        var dropped = new FakeBrowser();
        var reconnected = new FakeBrowser();
        await using var manager = new BrowserSessionManager(new McpServerLaunchOptions { Endpoint = Endpoint })
        {
            ConnectOverride = (_, _) => Task.FromResult<IBrowser>(
                dropped.IsHealthy ? dropped : reconnected),
            LaunchOverride = _ => throw new InvalidOperationException(
                "A browser this session did not start cannot be started by it."),
        };

        Assert.AreSame(dropped, await manager.EnsureBrowserAsync());

        // The connection drops, which for an attached browser says nothing about whether the
        // browser itself is still there.
        dropped.IsHealthy = false;

        Assert.AreSame(reconnected, await manager.EnsureBrowserAsync());
        Assert.AreEqual(2, manager.Generation);
    }
}
