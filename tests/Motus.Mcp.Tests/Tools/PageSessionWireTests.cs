using System.IO.Pipelines;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Exercises the tab, context, history, dialog, and evaluate tools over the real
/// in-process MCP transport without launching a browser: that every tool is
/// advertised and that each input schema exposes the agent-facing parameters (and
/// not the injected ones).
/// </summary>
[TestClass]
public class PageSessionWireTests
{
    [TestMethod]
    public async Task Server_AdvertisesTheNewTools()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var names = (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();

            foreach (var expected in new[]
            {
                "tab_list", "tab_open", "tab_select", "tab_close",
                "context_list", "context_create", "context_select", "context_close",
                "go_back", "go_forward", "reload", "handle_dialog", "evaluate",
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

            AssertProperties(tools["tab_open"], has: ["url"]);
            AssertProperties(tools["tab_select"], has: ["index"]);
            AssertProperties(tools["tab_close"], has: ["index"]);
            AssertProperties(tools["context_create"], has: ["name"]);
            AssertProperties(tools["context_select"], has: ["name"]);
            AssertProperties(tools["context_close"], has: ["name"]);
            AssertProperties(tools["handle_dialog"], has: ["accept", "text"]);
            AssertProperties(tools["evaluate"], has: ["expression", "ref"]);
        });
    }

    private static void AssertProperties(McpClientTool tool, string[] has)
    {
        var properties = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

        foreach (var name in has)
            CollectionAssert.Contains(properties, name);

        CollectionAssert.DoesNotContain(properties, "pageService");
        CollectionAssert.DoesNotContain(properties, "dialogService");
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
