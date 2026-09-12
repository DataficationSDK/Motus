using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests;

/// <summary>
/// Pins the mapping from the server's own configuration onto the launch and context options the
/// browser is actually started and configured with. Everything the host can set has to arrive
/// somewhere, and a setting that quietly maps nowhere looks identical to one that does.
/// </summary>
[TestClass]
public class McpServerLaunchOptionsTests
{
    [TestMethod]
    public void ToLaunchOptions_CarriesTheBrowserToStart()
    {
        var options = new McpServerLaunchOptions
        {
            Headless = false,
            ExecutablePath = "/opt/browser",
            Channel = BrowserChannel.Firefox,
            UserDataDir = "/tmp/profile",
        };

        var launch = options.ToLaunchOptions();

        Assert.IsFalse(launch.Headless);
        Assert.AreEqual("/opt/browser", launch.ExecutablePath);
        Assert.AreEqual(BrowserChannel.Firefox, launch.Channel);
        Assert.AreEqual("/tmp/profile", launch.UserDataDir);
    }

    [TestMethod]
    public void ToLaunchOptions_HeadlessWithNoBrowserArgs_PassesNone()
    {
        Assert.IsNull(new McpServerLaunchOptions { Headless = true }.ToLaunchOptions().Args);
    }

    [TestMethod]
    public void ToLaunchOptions_BrowserArgs_FollowTheWindowSizing()
    {
        var options = new McpServerLaunchOptions
        {
            Headless = false,
            Viewport = new ViewportSize(1024, 768),
            BrowserArgs = ["--no-sandbox", "--disable-dev-shm-usage"],
        };

        var args = options.ToLaunchOptions().Args;

        Assert.IsNotNull(args);
        CollectionAssert.AreEqual(
            new[] { "--window-size=1024,768", "--no-sandbox", "--disable-dev-shm-usage" }, args.ToArray());
    }

    [TestMethod]
    public void ToLaunchOptions_BrowserArgsWhileHeadless_AreTheWholeCommandLine()
    {
        var options = new McpServerLaunchOptions { Headless = true, BrowserArgs = ["--no-sandbox"] };

        var args = options.ToLaunchOptions().Args;

        Assert.IsNotNull(args);
        CollectionAssert.AreEqual(new[] { "--no-sandbox" }, args.ToArray());
    }

    [TestMethod]
    public void ToContextOptions_CarriesTheEmulationSettings()
    {
        var state = new StorageState([], []);
        var options = new McpServerLaunchOptions
        {
            UserAgent = "Agent/1.0",
            Locale = "en-GB",
            TimezoneId = "Europe/Berlin",
            Proxy = new ProxySettings("http://127.0.0.1:8080", "localhost"),
            StorageState = state,
        };

        var context = options.ToContextOptions();

        Assert.AreEqual("Agent/1.0", context.UserAgent);
        Assert.AreEqual("en-GB", context.Locale);
        Assert.AreEqual("Europe/Berlin", context.TimezoneId);
        Assert.AreEqual("http://127.0.0.1:8080", context.Proxy?.Server);
        Assert.AreEqual("localhost", context.Proxy?.Bypass);
        Assert.AreSame(state, context.StorageState);
    }

    [TestMethod]
    public void ToContextOptions_NothingConfigured_LeavesTheDefaultsAlone()
    {
        var context = new McpServerLaunchOptions().ToContextOptions();

        Assert.IsNull(context.UserAgent);
        Assert.IsNull(context.Locale);
        Assert.IsNull(context.TimezoneId);
        Assert.IsNull(context.Proxy);
        Assert.IsNull(context.StorageState);
    }

    [TestMethod]
    public void Dialogs_DefaultsToLeavingTheDialogForTheAgent()
    {
        Assert.AreEqual("ask", new McpServerLaunchOptions().Dialogs);
    }

    /// <summary>
    /// The timeouts have no home on a page or a context, so the tools read them from the page
    /// service and pass them on each call. This is the seam that carries them there.
    /// </summary>
    [TestMethod]
    public void PageService_ReadsTheConfiguredTimeouts()
    {
        var sessions = new BrowserSessionManager(new McpServerLaunchOptions
        {
            ActionTimeout = 2_500,
            NavigationTimeout = 45_000,
        });
        var pages = new ActivePageService(sessions);

        Assert.AreEqual(2_500d, pages.ActionTimeout);
        Assert.AreEqual(45_000, pages.Navigation?.Timeout);
    }

    [TestMethod]
    public void PageService_NoTimeoutsConfigured_LeavesTheFrameworkDefaults()
    {
        var pages = new ActivePageService(new BrowserSessionManager(new McpServerLaunchOptions()));

        Assert.IsNull(pages.ActionTimeout);
        Assert.IsNull(pages.Navigation);
    }
}
