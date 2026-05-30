using System.IO.Pipelines;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Exercises the registered tools over the real in-process MCP transport without
/// launching a browser: that the five tools are advertised, that their input
/// schemas expose the agent-facing parameters (and not the injected ones), and
/// that a tool call round-trips back a result.
/// </summary>
[TestClass]
public class CoreToolsWireTests
{
    [TestMethod]
    public async Task Server_AdvertisesTheCoreTools()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var names = (await client.ListToolsAsync(cancellationToken: ct)).Select(t => t.Name).ToArray();

            foreach (var expected in new[] { "navigate", "snapshot", "click", "type", "screenshot" })
                CollectionAssert.Contains(names, expected);
        });
    }

    [TestMethod]
    public async Task ClickSchema_ExposesRef_AndExcludesInjectedParameters()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            var click = tools.Single(t => t.Name == "click");

            var properties = click.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

            CollectionAssert.Contains(properties, "ref");      // @ref is exposed as "ref"
            CollectionAssert.Contains(properties, "double");
            CollectionAssert.DoesNotContain(properties, "pageService");
            CollectionAssert.DoesNotContain(properties, "cancellationToken");
            CollectionAssert.DoesNotContain(properties, "@ref");
        });
    }

    [TestMethod]
    public async Task TypeSchema_ExposesAllTextInputs()
    {
        await WithClientAsync(async (client, ct) =>
        {
            var tools = await client.ListToolsAsync(cancellationToken: ct);
            var type = tools.Single(t => t.Name == "type");

            var properties = type.JsonSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name).ToArray();

            foreach (var expected in new[] { "ref", "text", "submit", "slowly" })
                CollectionAssert.Contains(properties, expected);
        });
    }

    [TestMethod]
    public async Task CallTool_RoundTripsAResult()
    {
        await WithClientAsync(async (client, ct) =>
        {
            // A data: URL needs no network, so navigation resolves quickly whether or
            // not a browser is present. Either outcome (success text or an error
            // result when no browser can be launched) returns a result through the
            // transport, which is what this asserts.
            var result = await client.CallToolAsync(
                "navigate",
                new Dictionary<string, object?> { ["url"] = "data:text/html,<p>hi</p>" },
                cancellationToken: ct);

            Assert.IsNotNull(result);
            Assert.IsTrue(result.Content.Count >= 1);
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
