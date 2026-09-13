using Motus;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests;

/// <summary>
/// Launches a real browser with the configuration a host can set and reads back what the page
/// actually sees. The mapping tests prove the values reach the options record; these prove the
/// options record reaches the browser.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class LaunchConfigurationIntegrationTests
{
    private const string CustomUserAgent = "Motus-Launch-Check/1.0";

    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;

    [TestCleanup]
    public async Task Cleanup()
    {
        _pages?.Shutdown();

        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    /// <summary>
    /// The extra browser argument a container needs is the one worth proving arrives, and the user
    /// agent is the cheapest thing the page can be asked about. A browser that refused the argument
    /// would not have started, so one navigation answers both.
    /// </summary>
    [TestMethod]
    public async Task BrowserArgAndUserAgent_ReachTheRunningBrowser()
    {
        var service = StartSession(new McpServerLaunchOptions
        {
            Headless = true,
            ExecutablePath = ResolveBrowser(),
            BrowserArgs = ["--no-sandbox"],
            UserAgent = CustomUserAgent,
        });

        var navigate = await CoreTools.NavigateAsync(
            "data:text/html,<p>launch configuration</p>", service, CancellationToken.None);
        Assert.IsFalse(navigate.IsError ?? false, "navigate should succeed.");

        var evaluated = await PageTools.EvaluateAsync(
            expression: "navigator.userAgent",
            pageService: service,
            cancellationToken: CancellationToken.None,
            @ref: null);

        Assert.IsFalse(evaluated.IsError ?? false, "evaluate should succeed.");
        Assert.IsNotNull(evaluated.StructuredContent);
        Assert.AreEqual(
            CustomUserAgent, evaluated.StructuredContent.Value.GetProperty("result").GetString());
    }

    /// <summary>
    /// The locale and the time zone ride a different route from the user agent, through emulation
    /// overrides rather than the command line, so a page is asked about both. What they move is
    /// how the page formats dates and numbers, which is what the overrides set; they leave
    /// <c>navigator.language</c> where the browser build put it.
    /// </summary>
    [TestMethod]
    public async Task LocaleAndTimezone_ReachTheRunningBrowser()
    {
        var service = StartSession(new McpServerLaunchOptions
        {
            Headless = true,
            ExecutablePath = ResolveBrowser(),
            Locale = "en-GB",
            TimezoneId = "Europe/Berlin",
        });

        var navigate = await CoreTools.NavigateAsync(
            "data:text/html,<p>locale</p>", service, CancellationToken.None);
        Assert.IsFalse(navigate.IsError ?? false, "navigate should succeed.");

        var resolved = await EvaluateStringAsync(service, "Intl.DateTimeFormat().resolvedOptions().locale");
        Assert.AreEqual("en-GB", resolved);

        var timeZone = await EvaluateStringAsync(service, "Intl.DateTimeFormat().resolvedOptions().timeZone");
        Assert.AreEqual("Europe/Berlin", timeZone);
    }

    private static async Task<string?> EvaluateStringAsync(ActivePageService service, string expression)
    {
        var evaluated = await PageTools.EvaluateAsync(
            expression: expression,
            pageService: service,
            cancellationToken: CancellationToken.None,
            @ref: null);

        Assert.IsFalse(evaluated.IsError ?? false, $"evaluate of '{expression}' should succeed.");
        Assert.IsNotNull(evaluated.StructuredContent);
        return evaluated.StructuredContent.Value.GetProperty("result").GetString();
    }

    private ActivePageService StartSession(McpServerLaunchOptions options)
    {
        _sessions = new BrowserSessionManager(options);
        _pages = new ActivePageService(_sessions);
        return _pages;
    }

    /// <summary>
    /// The browser to drive: one downloaded by the install command when there is a marker for it,
    /// otherwise one the machine already has. Marks the test inconclusive when there is neither,
    /// which is this suite's standard skip gate.
    /// </summary>
    private static string ResolveBrowser()
    {
        var marker = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".motus", "browsers", ".installed.chromium");

        if (File.Exists(marker))
        {
            var installed = File.ReadAllText(marker).Trim();
            if (File.Exists(installed))
                return installed;
        }

        try
        {
            return BrowserFinder.Resolve(channel: null, executablePath: null);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
            throw;
        }
    }
}
