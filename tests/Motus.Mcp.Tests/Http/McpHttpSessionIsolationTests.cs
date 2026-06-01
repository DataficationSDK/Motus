using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;
using Motus.Mcp.Http;

namespace Motus.Mcp.Tests.Http;

/// <summary>
/// The defining M4A correctness check: two clients connected to the same HTTP host at once get
/// fully isolated browsers, and a client's session (and its browser) is torn down when it
/// disconnects. Drives real Chromium, so it is an integration test.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class McpHttpSessionIsolationTests
{
    private string? _executablePath;

    [TestInitialize]
    public void Setup()
    {
        _executablePath = ResolveInstalledBrowser();
        if (_executablePath is null)
            Assert.Inconclusive("No installed browser found; skipping integration test.");
    }

    [TestMethod]
    public async Task TwoClients_GetIsolatedSessions_AndCleanUpOnDisconnect()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        await using var server = await McpHttpServerHost.StartForTestingAsync(
            new McpHttpServerOptions
            {
                Host = "127.0.0.1",
                Port = 0,
                LaunchOptions = new McpServerLaunchOptions { Headless = true, ExecutablePath = _executablePath },
            },
            cts.Token);

        var alice = await ConnectAsync(server.BaseAddress, cts.Token);
        var bob = await ConnectAsync(server.BaseAddress, cts.Token);
        try
        {
            // Each client drives its own page. With isolated sessions, neither sees the other's.
            await NavigateAsync(alice, "data:text/html,<h1>AlphaPage</h1>", cts.Token);
            await NavigateAsync(bob, "data:text/html,<h1>BravoPage</h1>", cts.Token);

            var aliceView = await ListTabsAsync(alice, cts.Token);
            var bobView = await ListTabsAsync(bob, cts.Token);

            StringAssert.Contains(aliceView, "AlphaPage");
            Assert.IsFalse(aliceView.Contains("BravoPage"), "alice must not see bob's page");

            StringAssert.Contains(bobView, "BravoPage");
            Assert.IsFalse(bobView.Contains("AlphaPage"), "bob must not see alice's page");

            Assert.AreEqual(2, server.Registry.Count, "both sessions should be live");
        }
        finally
        {
            await bob.DisposeAsync();
        }

        // Disconnecting bob ends his session; the host disposes his bundle (and browser). Poll
        // because the teardown runs asynchronously once the transport closes.
        await WaitForCountAsync(server.Registry, expected: 1, cts.Token);

        await alice.DisposeAsync();
        await WaitForCountAsync(server.Registry, expected: 0, cts.Token);
    }

    private static async Task WaitForCountAsync(McpSessionRegistry registry, int expected, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (registry.Count != expected)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Expected {expected} live session(s) after disconnect; saw {registry.Count}.");
            await Task.Delay(100, ct);
        }
    }

    private static async Task<McpClient> ConnectAsync(Uri baseAddress, CancellationToken ct)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = baseAddress,
            TransportMode = HttpTransportMode.StreamableHttp,
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private static async Task NavigateAsync(McpClient client, string url, CancellationToken ct)
    {
        var result = await client.CallToolAsync(
            "navigate",
            new Dictionary<string, object?> { ["url"] = url },
            cancellationToken: ct);
        Assert.AreNotEqual(true, result.IsError, $"navigate failed: {TextOf(result)}");
    }

    private static async Task<string> ListTabsAsync(McpClient client, CancellationToken ct)
    {
        // tab_list takes no parameters and reports each open tab's URL, so it reflects the
        // session's own page without depending on optional tool arguments.
        var result = await client.CallToolAsync(
            "tab_list",
            new Dictionary<string, object?>(),
            cancellationToken: ct);
        Assert.AreNotEqual(true, result.IsError, $"tab_list failed: {TextOf(result)}");
        return TextOf(result);
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));

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
                continue;

            var executablePath = File.ReadAllText(markerPath).Trim();
            if (File.Exists(executablePath))
                return executablePath;
        }

        return null;
    }
}
