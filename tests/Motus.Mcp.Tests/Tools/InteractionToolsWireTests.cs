using System.IO.Pipelines;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Exercises the interaction tools over the real in-process MCP transport without
/// launching a browser: that every tool is advertised and that each input schema
/// exposes the agent-facing parameters (and not the injected ones).
/// </summary>
[TestClass]
public class InteractionToolsWireTests
{
    [TestMethod]
    public async Task Server_AdvertisesTheInteractionTools()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var names = (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();

            foreach (var expected in new[]
            {
                "select_option", "hover", "press", "set_checked", "clear", "focus",
                "scroll_into_view", "upload_files", "press_key", "wait_for_element", "wait_for",
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

            AssertProperties(tools["select_option"], has: ["ref", "values"]);
            AssertProperties(tools["set_checked"], has: ["ref", "checked"]);
            AssertProperties(tools["press"], has: ["ref", "key"]);
            AssertProperties(tools["upload_files"], has: ["ref", "paths"]);
            AssertProperties(tools["press_key"], has: ["key"]);
            AssertProperties(tools["wait_for_element"], has: ["ref", "state"]);
            AssertProperties(tools["wait_for"], has: ["time", "text", "text_gone"]);
        });
    }

    private static void AssertProperties(McpClientTool tool, string[] has)
    {
        var properties = tool.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

        foreach (var name in has)
            CollectionAssert.Contains(properties, name);

        CollectionAssert.DoesNotContain(properties, "pageService");
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
