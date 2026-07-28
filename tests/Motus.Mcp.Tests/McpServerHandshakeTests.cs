using System.IO.Pipelines;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests;

[TestClass]
public class McpServerHandshakeTests
{
    /// <summary>
    /// Runs the real server wiring over an in-process stream transport and drives
    /// it with an MCP client. The client completing <c>CreateAsync</c> means the
    /// initialize handshake succeeded and capabilities were exchanged. No browser
    /// is launched, since no tool that needs a page is invoked.
    /// </summary>
    [TestMethod]
    public async Task Server_CompletesInitializeHandshake_AndReportsServerInfo()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Two one-directional pipes wired into a duplex channel.
        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        // The server reads what the client writes and writes what the client reads.
        var hostTask = McpServerHost.RunAsync(
            new McpServerLaunchOptions(),
            builder => builder.WithStreamServerTransport(
                clientToServer.Reader.AsStream(),
                serverToClient.Writer.AsStream()),
            cts.Token);

        // StreamClientTransport(serverInput, serverOutput): the first stream is
        // what the server reads (the client writes to it), the second is what the
        // server writes (the client reads from it).
        var clientTransport = new StreamClientTransport(
            serverInput: clientToServer.Writer.AsStream(),
            serverOutput: serverToClient.Reader.AsStream());

        try
        {
            await using var client = await McpClient.CreateAsync(
                clientTransport,
                cancellationToken: cts.Token);

            Assert.AreEqual("motus", client.ServerInfo.Name);
            Assert.IsFalse(string.IsNullOrWhiteSpace(client.ServerInfo.Version));
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

    /// <summary>
    /// No tool may advertise a parameter that accepts null as one the caller must supply.
    /// </summary>
    /// <remarks>
    /// A parameter with no default is advertised as required whatever its type, so a nullable one
    /// ends up required and accepting null at the same time. A client that reasonably omits it
    /// gets a hard argument error rather than the default behavior the description promises. The
    /// whole catalog is checked rather than a sample, because the trap is in the method signature
    /// and reappears the moment a new tool is written the same way.
    /// </remarks>
    [TestMethod]
    public async Task NoTool_RequiresAParameterThatAcceptsNull()
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
            await using var client = await McpClient.CreateAsync(
                clientTransport,
                cancellationToken: cts.Token);

            var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
            Assert.IsTrue(tools.Count > 0, "The server advertised no tools.");

            var offenders = new List<string>();

            foreach (var tool in tools)
            {
                var schema = tool.ProtocolTool.InputSchema;
                if (!schema.TryGetProperty("required", out var required)
                    || required.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                schema.TryGetProperty("properties", out var properties);

                foreach (var name in required.EnumerateArray())
                {
                    var key = name.GetString();
                    if (key is null
                        || !properties.TryGetProperty(key, out var property)
                        || !property.TryGetProperty("type", out var type)
                        || type.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    if (type.EnumerateArray().Any(t => t.GetString() == "null"))
                        offenders.Add($"{tool.Name}.{key}");
                }
            }

            Assert.AreEqual(0, offenders.Count,
                "These parameters accept null but are advertised as required, so a client that "
                + $"omits them fails instead of getting the default: {string.Join(", ", offenders)}. "
                + "Give the parameter a default value, which also moves it after the required ones.");
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
