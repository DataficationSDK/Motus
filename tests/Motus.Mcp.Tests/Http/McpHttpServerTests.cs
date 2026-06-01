using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp.Http;

namespace Motus.Mcp.Tests.Http;

/// <summary>
/// Drives the Streamable HTTP host with a real MCP client over a loopback socket. None of these
/// launch a browser: the handshake, tool advertisement, per-session service resolution, and auth
/// are all observable without one.
/// </summary>
[TestClass]
public class McpHttpServerTests
{
    [TestMethod]
    public async Task Http_CompletesHandshake_AndAdvertisesAllTools()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = await StartAsync(cts.Token);

        await using var client = await ConnectAsync(server, token: null, cts.Token);

        Assert.AreEqual("motus", client.ServerInfo.Name);

        var toolNames = (await client.ListToolsAsync(cancellationToken: cts.Token)).Select(t => t.Name).ToArray();

        // A representative tool from each registered class, proving the shared registration runs
        // over HTTP exactly as it does over stdio.
        foreach (var expected in new[]
                 {
                     "navigate", "snapshot", "click", "select_option", "tab_list", "go_back",
                     "route_list", "console_messages", "audit_accessibility", "get_performance",
                     "trace_start", "generate_pom",
                 })
        {
            CollectionAssert.Contains(toolNames, expected, $"tool '{expected}' should be advertised");
        }
    }

    /// <summary>
    /// The per-session validation gate. <c>console_messages</c> resolves the per-session
    /// <c>ConsoleService</c> through the registry's ambient bundle and drains it, with no browser
    /// involved. A friendly "no messages" result proves the ambient bundle flowed into the tool
    /// service factory; if it had not, the factory would have thrown resolving the service.
    /// </summary>
    [TestMethod]
    public async Task Http_PerSessionService_ResolvesFromAmbientBundle()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = await StartAsync(cts.Token);
        await using var client = await ConnectAsync(server, token: null, cts.Token);

        var result = await client.CallToolAsync(
            "console_messages",
            new Dictionary<string, object?>(),
            cancellationToken: cts.Token);

        Assert.AreNotEqual(true, result.IsError, "the per-session ConsoleService should have resolved");
        StringAssert.Contains(TextOf(result), "No console messages");
    }

    [TestMethod]
    public async Task Http_WithToken_RejectsMissingAndWrongBearer_AcceptsCorrect()
    {
        const string token = "s3cret-token";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var server = await StartAsync(cts.Token, token);

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            async () => await ConnectAsync(server, token: null, cts.Token),
            "a client with no bearer token should be rejected");

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            async () => await ConnectAsync(server, token: "wrong", cts.Token),
            "a client with the wrong bearer token should be rejected");

        await using var client = await ConnectAsync(server, token, cts.Token);
        Assert.AreEqual("motus", client.ServerInfo.Name);
    }

    [TestMethod]
    public void Build_NonLoopbackWithoutToken_IsRefused()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => McpHttpServerHost.Build(new McpHttpServerOptions { Host = "0.0.0.0", Port = 0 }));
        StringAssert.Contains(ex.Message, "token");
    }

    private static Task<McpHttpServerHost.RunningServer> StartAsync(CancellationToken ct, string? token = null)
        => McpHttpServerHost.StartForTestingAsync(
            new McpHttpServerOptions { Host = "127.0.0.1", Port = 0, Token = token },
            ct);

    private static async Task<McpClient> ConnectAsync(
        McpHttpServerHost.RunningServer server,
        string? token,
        CancellationToken ct)
    {
        var options = new HttpClientTransportOptions
        {
            Endpoint = server.BaseAddress,
            TransportMode = HttpTransportMode.StreamableHttp,
        };
        if (token is not null)
        {
            options.AdditionalHeaders = new Dictionary<string, string>
            {
                ["Authorization"] = $"Bearer {token}",
            };
        }

        var transport = new HttpClientTransport(options);
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }

    private static string TextOf(CallToolResult result)
        => string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
}
