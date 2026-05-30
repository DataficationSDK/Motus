using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests;

[TestClass]
[TestCategory("Integration")]
public class BrowserSessionManagerTests
{
    private static BrowserSessionManager NewManager()
        => new(new McpServerLaunchOptions { Headless = true, ExecutablePath = ResolveInstalledBrowser() });

    /// <summary>
    /// Resolves a browser downloaded by the install command from its cache marker,
    /// or null to let the framework auto-detect a system browser.
    /// </summary>
    private static string? ResolveInstalledBrowser()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".motus",
            "browsers");

        foreach (var marker in new[] { ".installed.chromium", ".installed" })
        {
            var markerPath = Path.Combine(cacheDir, marker);
            if (!File.Exists(markerPath))
            {
                continue;
            }

            var executablePath = File.ReadAllText(markerPath).Trim();
            if (File.Exists(executablePath))
            {
                return executablePath;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a manager and launches its browser, or marks the test inconclusive
    /// when no browser is installed (the repo's standard skip gate). Returns null
    /// when skipped so callers can return early.
    /// </summary>
    private static async Task<BrowserSessionManager?> TryLaunchAsync()
    {
        var manager = NewManager();
        try
        {
            await manager.EnsureBrowserAsync();
            return manager;
        }
        catch (FileNotFoundException)
        {
            await manager.DisposeAsync();
            Assert.Inconclusive("No browser found; skipping integration test.");
            return null;
        }
    }

    [TestMethod]
    public async Task EnsureBrowserAsync_LaunchesOnce()
    {
        var manager = await TryLaunchAsync();
        if (manager is null) return;
        await using var _ = manager;

        var first = await manager.EnsureBrowserAsync();
        var second = await manager.EnsureBrowserAsync();

        Assert.AreSame(first, second);
        Assert.IsTrue(manager.IsBrowserLaunched);
    }

    [TestMethod]
    public async Task GetOrCreateActiveContext_CreatesDefaultImplicitly()
    {
        var manager = await TryLaunchAsync();
        if (manager is null) return;
        await using var _ = manager;

        var context = await manager.GetOrCreateActiveContextAsync();

        Assert.IsNotNull(context);
        Assert.AreEqual(BrowserSessionManager.DefaultContextName, manager.ActiveContextName);
        CollectionAssert.Contains(manager.ContextNames.ToList(), BrowserSessionManager.DefaultContextName);
    }

    [TestMethod]
    public async Task CreateContext_BecomesActive_AndSelectSwitchesBack()
    {
        var manager = await TryLaunchAsync();
        if (manager is null) return;
        await using var _ = manager;

        await manager.GetOrCreateActiveContextAsync();
        await manager.CreateContextAsync("userB");
        Assert.AreEqual("userB", manager.ActiveContextName);

        manager.SelectContext(BrowserSessionManager.DefaultContextName);
        Assert.AreEqual(BrowserSessionManager.DefaultContextName, manager.ActiveContextName);
    }

    [TestMethod]
    public async Task CreateContext_DuplicateName_Throws()
    {
        var manager = await TryLaunchAsync();
        if (manager is null) return;
        await using var _ = manager;

        await manager.CreateContextAsync("userB");
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => manager.CreateContextAsync("userB"));
    }

    [TestMethod]
    public async Task CloseActiveContext_FallsBackToDefault()
    {
        var manager = await TryLaunchAsync();
        if (manager is null) return;
        await using var _ = manager;

        await manager.GetOrCreateActiveContextAsync();
        await manager.CreateContextAsync("userB");

        await manager.CloseContextAsync("userB");

        Assert.AreEqual(BrowserSessionManager.DefaultContextName, manager.ActiveContextName);
        CollectionAssert.DoesNotContain(manager.ContextNames.ToList(), "userB");
    }

    [TestMethod]
    public async Task DisposeAsync_TearsDownBrowser()
    {
        var manager = await TryLaunchAsync();
        if (manager is null) return;

        var browser = await manager.EnsureBrowserAsync();
        await manager.DisposeAsync();

        Assert.IsFalse(browser.IsConnected);
        Assert.IsFalse(manager.IsBrowserLaunched);
    }
}
