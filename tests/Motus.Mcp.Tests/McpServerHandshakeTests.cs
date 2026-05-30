using System.IO.Pipelines;
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
}
