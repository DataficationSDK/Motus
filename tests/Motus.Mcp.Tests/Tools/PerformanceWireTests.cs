using System.IO.Pipelines;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Exercises the performance tool over the real in-process MCP transport without
/// launching a browser: that it is advertised and that its input schema does not
/// expose the injected parameters.
/// </summary>
[TestClass]
public class PerformanceWireTests
{
    [TestMethod]
    public async Task Server_AdvertisesGetPerformance()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var names = (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();
            CollectionAssert.Contains(names, "get_performance");
        });
    }

    [TestMethod]
    public async Task PerformanceSchema_ExcludesInjectedParameters()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            var perf = tools.Single(t => t.Name == "get_performance");

            // The tool takes no agent-facing parameters, so the injected service and the
            // cancellation token must not surface in the schema.
            var properties = perf.JsonSchema.TryGetProperty("properties", out var props)
                ? props.EnumerateObject().Select(p => p.Name).ToArray()
                : [];

            CollectionAssert.DoesNotContain(properties, "pageService");
            CollectionAssert.DoesNotContain(properties, "cancellationToken");
        });
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
