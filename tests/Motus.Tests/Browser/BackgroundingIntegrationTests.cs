using Motus.Abstractions;

namespace Motus.Tests.Browser;

/// <summary>
/// Pins that a page in one context keeps running while other contexts come and go.
/// </summary>
/// <remarks>
/// Each context gets a window of its own. Chromium marked a page hidden once a second context had
/// opened and closed, so its <c>visibilitychange</c> handlers saw <c>hidden</c>, its timers were
/// throttled, and code waiting for the page to be visible waited until the test timed out. Not
/// every build does it: Chrome for Testing 149 headless did on every run, and Chrome 156 headless
/// did not, so on some browsers these tests pass either way.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class BackgroundingIntegrationTests
{
    private const string TimerInterval = "50";

    /// <summary>
    /// Counts timer ticks, records every visibility change, and has nothing else in it, so the
    /// numbers read back describe the page and not its content.
    /// </summary>
    private const string CountingDocument =
        "data:text/html,<script>"
        + "window.ticks=0;window.visibilityChanges=[];"
        + "setInterval(()=>window.ticks++," + TimerInterval + ");"
        + "document.addEventListener('visibilitychange',()=>window.visibilityChanges.push(document.visibilityState));"
        + "</script><h1>counting</h1>";

    private static readonly string[] BackgroundingSwitches =
    [
        "--disable-backgrounding-occluded-windows",
        "--disable-renderer-backgrounding",
        "--disable-background-timer-throttling",
    ];

    private IBrowser? _browser;

    private async Task<IBrowser> LaunchAsync(LaunchOptions? options = null)
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(options ?? new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
        }

        if (_browser!.Version.Contains("Firefox", StringComparison.OrdinalIgnoreCase))
            Assert.Inconclusive("Chromium's backgrounding does not apply to Firefox.");

        return _browser;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    [TestMethod]
    public async Task ASecondContextOpeningAndClosing_LeavesThePageInTheFirstVisibleAndRunning()
    {
        var browser = await LaunchAsync();

        var first = await browser.NewContextAsync();
        var page = await first.NewPageAsync();
        await page.GotoAsync(CountingDocument);

        var second = await browser.NewContextAsync();
        var other = await second.NewPageAsync();
        await other.GotoAsync("data:text/html,<h1>second user</h1>");
        await Task.Delay(500);
        await second.CloseAsync();

        // Measured over a window that starts after the second context is gone, which is when the
        // first page used to be marked hidden.
        await Task.Delay(300);
        var ticksBefore = await page.EvaluateAsync<int>("window.ticks");
        await Task.Delay(1500);
        var ticksAfter = await page.EvaluateAsync<int>("window.ticks");

        var changes = await page.EvaluateAsync<string>("window.visibilityChanges.join(',')");
        Assert.AreEqual("", changes, "the page never stopped being visible");
        Assert.AreEqual("visible", await page.EvaluateAsync<string>("document.visibilityState"));

        // 1.5 seconds of a 50 ms interval is 30 ticks. A hidden page managed about 12.
        var ticks = ticksAfter - ticksBefore;
        Assert.IsTrue(ticks >= 20, $"the page's timers kept their cadence, but only {ticks} ticks ran in 1.5 seconds");
    }

    [TestMethod]
    public async Task EveryContextsPage_HasFocusAtOnce()
    {
        var browser = await LaunchAsync();

        var admin = await (await browser.NewContextAsync()).NewPageAsync();
        var visitor = await (await browser.NewContextAsync()).NewPageAsync();
        await admin.GotoAsync("data:text/html,<h1>admin</h1>");
        await visitor.GotoAsync("data:text/html,<h1>visitor</h1>");

        Assert.IsTrue(await admin.EvaluateAsync<bool>("document.hasFocus()"), "the first context's page has focus");
        Assert.IsTrue(await visitor.EvaluateAsync<bool>("document.hasFocus()"), "the second context's page has focus");
    }

    [TestMethod]
    public async Task LaunchedBrowser_RunsWithTheBackgroundingSwitches()
    {
        var commandLine = await ReadCommandLineAsync(await LaunchAsync());

        foreach (var flag in BackgroundingSwitches)
            StringAssert.Contains(commandLine, flag);
    }

    [TestMethod]
    public async Task IgnoreDefaultArgs_KeepsTheBackgroundingSwitchesOffTheCommandLine()
    {
        var commandLine = await ReadCommandLineAsync(await LaunchAsync(new LaunchOptions
        {
            Headless = true,
            IgnoreDefaultArgs = BackgroundingSwitches,
        }));

        foreach (var flag in BackgroundingSwitches)
            Assert.IsFalse(commandLine.Contains(flag, StringComparison.Ordinal), $"{flag} was ignored but still passed");
    }

    /// <summary>
    /// Reads the command line the browser reports it was started with, which is what it acted on,
    /// rather than the list Motus built.
    /// </summary>
    private static async Task<string> ReadCommandLineAsync(IBrowser browser)
    {
        var page = await browser.NewPageAsync();
        await page.GotoAsync("chrome://version");
        var commandLine = await page.EvaluateAsync<string>(
            "document.getElementById('command_line')?.textContent ?? ''");

        if (string.IsNullOrEmpty(commandLine))
            Assert.Inconclusive("This browser's version page does not show its command line.");

        return commandLine;
    }
}
