using System.IO.Pipelines;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Exercises the network and console tools over the real in-process MCP transport
/// without launching a browser: that every tool is advertised and that each input
/// schema exposes the agent-facing parameters (and not the injected ones).
/// </summary>
[TestClass]
public class NetworkConsoleWireTests
{
    [TestMethod]
    public async Task Server_AdvertisesTheNewTools()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var names = (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();

            foreach (var expected in new[]
            {
                "route_fulfill", "route_abort", "route_continue", "unroute", "route_list",
                "network_requests", "console_messages",
            })
                CollectionAssert.Contains(names, expected);
        });
    }

    [TestMethod]
    public async Task Schemas_ExposeAgentParameters_AndExcludeInjected()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var tools = (await client.ListToolsAsync(cancellationToken: ct)).ToDictionary(t => t.Name);

            AssertProperties(tools["route_fulfill"], has: ["url_pattern", "status", "body", "content_type", "headers"]);
            AssertProperties(tools["route_abort"], has: ["url_pattern", "error_code"]);
            AssertProperties(tools["route_continue"], has: ["url_pattern", "url", "method", "headers", "post_data"]);
            AssertProperties(tools["unroute"], has: ["url_pattern"]);
            AssertProperties(tools["route_list"], has: []);
            AssertProperties(tools["network_requests"], has: []);
            AssertProperties(tools["console_messages"], has: []);
        });
    }

    private static void AssertProperties(McpClientTool tool, string[] has)
    {
        var schema = tool.JsonSchema;
        var properties = schema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().Select(p => p.Name).ToArray()
            : [];

        foreach (var name in has)
            CollectionAssert.Contains(properties, name);

        CollectionAssert.DoesNotContain(properties, "pageService");
        CollectionAssert.DoesNotContain(properties, "networkService");
        CollectionAssert.DoesNotContain(properties, "consoleService");
        CollectionAssert.DoesNotContain(properties, "cancellationToken");
    }

    private static async Task WithClientAsync(Func<McpClient, CancellationToken, Task> body)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var hostTask = McpServerHost.RunAsync(
            new McpServerLaunchOptions(),
            builder => builder.WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream()),
            cts.Token);

        var clientTransport = new ModelContextProtocol.Protocol.StreamClientTransport(
            serverInput: clientToServer.Writer.AsStream(),
            serverOutput: serverToClient.Reader.AsStream());

        try
        {
            await using var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cts.Token);
            await body(client, cts.Token);
        }
        finally
        {
            await cts.CancelAsync();
            try
            {
                await hostTask;
            }
            catch (OperationCanceledException)
            {
                // Expected: cancelling the token shuts the host down.
            }
        }
    }
}
