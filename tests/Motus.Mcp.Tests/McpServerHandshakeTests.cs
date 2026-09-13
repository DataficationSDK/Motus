using System.IO.Pipelines;
using System.Linq;
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

    /// <summary>
    /// Every tool description is sent to the client on connection whether the agent calls the
    /// tool or not, so what the catalog holds by default is a standing cost worth pinning. This
    /// names the whole default set: a tool added without a decision about which group it belongs
    /// to shows up here as a surprise rather than quietly in everybody's context window.
    /// </summary>
    [TestMethod]
    public async Task DefaultCatalog_HoldsTheToolsEverySessionNeeds_AndNothingElse()
    {
        var names = await ListToolNamesAsync(new McpServerLaunchOptions());

        CollectionAssert.AreEquivalent(DefaultTools, names,
            "Default catalog: " + string.Join(", ", names.Order()));
    }

    [TestMethod]
    public async Task AskingForRecording_AddsTheRecordingTools_AndNothingElse()
    {
        var names = await ListToolNamesAsync(
            new McpServerLaunchOptions { Capabilities = [ToolCapabilities.Recording] });

        CollectionAssert.AreEquivalent(
            DefaultTools.Concat(["trace_start", "trace_stop", "har_start", "har_stop", "video_start", "video_stop"]).ToArray(),
            names,
            "With recording: " + string.Join(", ", names.Order()));
    }

    /// <summary>
    /// Attaching follows the option that allows it rather than a group of its own: a tool that
    /// would refuse every call is left out, so an agent is not taught to try it.
    /// </summary>
    [TestMethod]
    public async Task AllowingAttach_AddsTheAttachTool()
    {
        CollectionAssert.DoesNotContain(await ListToolNamesAsync(new McpServerLaunchOptions()), "browser_attach");

        var allowed = await ListToolNamesAsync(new McpServerLaunchOptions { AllowAttach = true });
        CollectionAssert.Contains(allowed, "browser_attach");

        var connected = await ListToolNamesAsync(
            new McpServerLaunchOptions { Endpoint = "http://127.0.0.1:9222" });
        CollectionAssert.Contains(connected, "browser_attach");
    }

    /// <summary>The catalog a server with no options advertises.</summary>
    private static readonly string[] DefaultTools =
    [
        "navigate", "snapshot", "click", "type", "screenshot",
        "select_option", "hover", "press", "set_checked", "clear", "focus", "scroll_into_view",
        "upload_files", "press_key", "wait_for_element", "wait_for",
        "tab_list", "tab_open", "tab_select", "tab_close", "browser_status",
        "frame_list", "frame_select",
        "go_back", "go_forward", "reload", "handle_dialog", "evaluate",
        "network_requests", "network_request",
        "console_messages",
        "audit_accessibility",
        "get_performance",
        "generate_pom",
    ];

    /// <summary>
    /// Starts a server with the given options over an in-process transport and returns the tool
    /// names it advertises. No browser is launched, since no tool is called.
    /// </summary>
    private static async Task<string[]> ListToolNamesAsync(McpServerLaunchOptions options)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var clientToServer = new Pipe();
        var serverToClient = new Pipe();

        var hostTask = McpServerHost.RunAsync(
            options,
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
            var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
            return tools.Select(t => t.Name).ToArray();
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
