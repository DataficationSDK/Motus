using System.IO.Pipelines;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Exercises the recording and codegen tools over the real in-process MCP transport
/// without launching a browser: that they are advertised and that their input schemas
/// expose the agent parameters while hiding the injected ones.
/// </summary>
[TestClass]
public class ArtifactWireTests
{
    [TestMethod]
    public async Task Server_AdvertisesArtifactTools()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var names = (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();
            CollectionAssert.Contains(names, "trace_start");
            CollectionAssert.Contains(names, "trace_stop");
            CollectionAssert.Contains(names, "har_start");
            CollectionAssert.Contains(names, "har_stop");
            CollectionAssert.Contains(names, "generate_pom");
        });
    }

    [TestMethod]
    public async Task ArtifactSchemas_ExposeAgentParametersAndHideInjectedOnes()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var tools = (await client.ListToolsAsync(cancellationToken: ct)).ToDictionary(t => t.Name);

            var traceStart = Properties(tools["trace_start"]);
            CollectionAssert.Contains(traceStart, "screenshots");
            CollectionAssert.Contains(traceStart, "snapshots");

            var traceStop = Properties(tools["trace_stop"]);
            CollectionAssert.Contains(traceStop, "path");

            // har_start takes no agent parameters.
            CollectionAssert.DoesNotContain(Properties(tools["har_start"]), "path");

            var harStop = Properties(tools["har_stop"]);
            CollectionAssert.Contains(harStop, "path");

            var pom = Properties(tools["generate_pom"]);
            CollectionAssert.Contains(pom, "namespace");
            CollectionAssert.Contains(pom, "class_name");

            // The injected service and cancellation token never surface on any of them.
            foreach (var name in new[] { "trace_start", "trace_stop", "har_start", "har_stop", "generate_pom" })
            {
                var props = Properties(tools[name]);
                CollectionAssert.DoesNotContain(props, "pageService");
                CollectionAssert.DoesNotContain(props, "cancellationToken");
            }
        });
    }

    private static string[] Properties(McpClientTool tool)
        => tool.JsonSchema.TryGetProperty("properties", out var props)
            ? props.EnumerateObject().Select(p => p.Name).ToArray()
            : [];

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

        var clientTransport = new StreamClientTransport(
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
