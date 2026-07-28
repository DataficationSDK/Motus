using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Motus.Abstractions;

namespace Motus.Tests.Browser;

/// <summary>
/// Covers connecting to a browser Motus did not start: seeing what is already open in it, keeping
/// that view accurate, and never ending a browser that belongs to somebody else.
/// </summary>
/// <remarks>
/// The browser under test is started here rather than through Motus, because ownership is the
/// point. A browser handed over by <c>LaunchAsync</c> would be owned, and the distinction these
/// tests exist to pin would not be real.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class BrowserAttachIntegrationTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SettleTimeout = TimeSpan.FromSeconds(10);

    private Process? _process;
    private string? _userDataDir;
    private int _port;

    private string HttpEndpoint => $"http://127.0.0.1:{_port}";

    [TestInitialize]
    public async Task Setup()
    {
        string executablePath;
        try
        {
            executablePath = BrowserFinder.Resolve(channel: null, executablePath: null);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found on this machine.");
            return;
        }

        _port = AllocateFreePort();
        _userDataDir = Path.Combine(Path.GetTempPath(), "motus-attach-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_userDataDir);

        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var arg in ChromiumArgs.Build(new LaunchOptions { Headless = true }, _port, _userDataDir))
            psi.ArgumentList.Add(arg);

        _process = Process.Start(psi);
        Assert.IsNotNull(_process, "The browser process did not start.");

        // Redirected streams nobody reads fill and then block the browser writing to them.
        BrowserOutputDrain.Start(_process.StandardOutput, _process.StandardError);

        await CdpEndpointPoller.WaitForEndpointAsync(_port, StartupTimeout, CancellationToken.None);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(entireProcessTree: true); } catch { /* already gone */ }
            }

            _process.Dispose();
            _process = null;
        }

        if (_userDataDir is not null)
        {
            try { Directory.Delete(_userDataDir, recursive: true); } catch { /* best-effort */ }
            _userDataDir = null;
        }
    }

    [TestMethod]
    public async Task ConnectAsync_AdoptsExistingContextsAndPages()
    {
        // The HTTP form is what a caller who chose the port already has, so it is the one used
        // here: this also covers resolving the WebSocket URL from it.
        await using var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        Assert.IsTrue(browser.IsConnected);
        Assert.IsTrue(browser.Contexts.Count > 0, "Connecting adopted no contexts.");

        var page = browser.Contexts.SelectMany(c => c.Pages).FirstOrDefault();
        Assert.IsNotNull(page, "Connecting adopted no pages.");

        // An adopted page is a working page, not just an entry in a list.
        await page.GotoAsync("data:text/html,<h1 id='greeting'>Hello</h1>");

        var text = await page.Locator("#greeting").TextContentAsync();
        Assert.AreEqual("Hello", text);
    }

    [TestMethod]
    public async Task ConnectAsync_AdoptedPageReportsItsUrl()
    {
        await using var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        var page = browser.Contexts.SelectMany(c => c.Pages).FirstOrDefault();
        Assert.IsNotNull(page, "Connecting adopted no pages.");

        // A page taken over mid-life has already navigated, so nothing replays its frame tree.
        // Without seeding it the main frame is missing and this reads as empty.
        Assert.IsFalse(string.IsNullOrEmpty(page.Url), "The adopted page reported no URL.");
        Assert.IsNotNull(page.MainFrame);
    }

    [TestMethod]
    public async Task ConnectAsync_WithAdoptionDisabled_HasNoContexts()
    {
        await using var browser = await MotusLauncher.ConnectAsync(
            HttpEndpoint, new ConnectOptions { AdoptExistingTargets = false });

        Assert.IsTrue(browser.IsConnected);
        Assert.AreEqual(0, browser.Contexts.Count);
    }

    [TestMethod]
    public async Task ConnectAsync_DoesNotOwnTheProcess()
    {
        await using var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        Assert.IsFalse(browser.OwnsProcess);
    }

    /// <summary>
    /// A browser Motus attached to belongs to whoever started it. Closing the handle must let go
    /// of it and nothing more.
    /// </summary>
    [TestMethod]
    public async Task CloseAsync_OnAttachedBrowser_LeavesBrowserRunning()
    {
        var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        await browser.CloseAsync();

        Assert.IsFalse(browser.IsConnected);
        Assert.IsFalse(_process!.HasExited, "Closing an attached browser ended a process Motus did not start.");
        Assert.IsTrue(await EndpointAnswersAsync(), "The browser stopped serving its debugging endpoint.");

        // Proof the browser is still usable, not merely still resident.
        await using var reconnected = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());
        Assert.IsTrue(reconnected.IsConnected);
    }

    [TestMethod]
    public async Task DisconnectAsync_LeavesBrowserRunning()
    {
        var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        await browser.DisconnectAsync();

        Assert.IsFalse(browser.IsConnected);
        Assert.IsFalse(_process!.HasExited);
        Assert.IsTrue(await EndpointAnswersAsync());
    }

    /// <summary>
    /// Disconnecting lets go of the contexts locally while leaving them open in the browser.
    /// </summary>
    /// <remarks>
    /// Handles left listed after a disconnect are backed by a transport that is gone, so every
    /// call through them fails obscurely. Letting go of them locally is not the same as closing
    /// them: the tab that was already open has to still be there afterwards, which the reconnect
    /// below is what proves.
    /// </remarks>
    [TestMethod]
    public async Task DisconnectAsync_ReleasesContextsLocallyButLeavesThemOpen()
    {
        var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());
        Assert.IsTrue(browser.Contexts.Count > 0, "Connecting adopted no context to release.");

        await browser.DisconnectAsync();

        Assert.AreEqual(0, browser.Contexts.Count,
            "Contexts are still listed after a disconnect, over a transport that is gone.");

        await using var reconnected = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());
        Assert.IsTrue(reconnected.Contexts.Count > 0,
            "Disconnecting took the browser's contexts with it.");
        Assert.IsTrue(reconnected.Contexts.Any(c => c.Pages.Count > 0),
            "The tab that was open before the disconnect is gone.");
    }

    [TestMethod]
    public async Task DisposeAsync_OnAttachedBrowser_LeavesBrowserRunning()
    {
        var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        await browser.DisposeAsync();

        Assert.IsFalse(_process!.HasExited);
        Assert.IsTrue(await EndpointAnswersAsync());
    }

    [TestMethod]
    public async Task PageOpenedAfterConnect_IsAdopted()
    {
        await using var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        var context = browser.Contexts.First(c => c.Pages.Count > 0);
        var page = context.Pages[0];
        var before = context.Pages.Count;

        // Opened by the page itself, so it is a tab Motus did not create.
        await page.EvaluateAsync<bool>("(() => { window.open('about:blank'); return true; })()");

        var adopted = await WaitForAsync(() => context.Pages.Count > before);
        Assert.IsTrue(adopted, "A page opened while attached never appeared.");
    }

    [TestMethod]
    public async Task PageClosedAfterConnect_IsRetired()
    {
        await using var browser = await MotusLauncher.ConnectAsync(HttpEndpoint, new ConnectOptions());

        var context = browser.Contexts.First(c => c.Pages.Count > 0);
        var opener = context.Pages[0];
        var before = context.Pages.Count;

        await opener.EvaluateAsync<bool>("(() => { window.open('about:blank'); return true; })()");
        Assert.IsTrue(await WaitForAsync(() => context.Pages.Count > before), "The opened page never appeared.");

        var opened = context.Pages.Last();

        // Closed from inside the browser, so the handle is retired by the target going away
        // rather than by Motus having asked for it.
        await opened.EvaluateAsync<bool>("(() => { window.close(); return true; })()");

        var retired = await WaitForAsync(() => !context.Pages.Contains(opened));
        Assert.IsTrue(retired, "A page closed while attached never went away.");
        Assert.IsTrue(opened.IsClosed, "The retired page does not report itself closed.");
    }

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + SettleTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return true;

            await Task.Delay(50);
        }

        return condition();
    }

    private async Task<bool> EndpointAnswersAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
            var json = await client.GetStringAsync($"{HttpEndpoint}/json/version");
            return json.Contains("webSocketDebuggerUrl", StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private static int AllocateFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
