using System.CommandLine;
using Motus.Mcp;
using Motus.Mcp.Http;

namespace Motus.Cli.Commands;

public static class McpCommand
{
    private const string TokenEnvVar = "MOTUS_MCP_TOKEN";

    public static Command Build()
    {
        var headlessOpt = new Option<bool>("--headless")
        {
            Description = "Run the browser without a visible window",
            DefaultValueFactory = _ => true,
        };
        var channelOpt = new Option<string>("--channel")
        {
            Description = "Browser channel to drive (chromium, chrome, edge, firefox)",
            DefaultValueFactory = _ => "chromium",
        };
        var httpOpt = new Option<bool>("--http")
        {
            Description = "Serve over Streamable HTTP for concurrent remote clients instead of stdio",
            DefaultValueFactory = _ => false,
        };
        var hostOpt = new Option<string>("--host")
        {
            Description = "Host/interface to bind when --http is set",
            DefaultValueFactory = _ => "127.0.0.1",
        };
        var portOpt = new Option<int>("--port")
        {
            Description = "TCP port to listen on when --http is set",
            DefaultValueFactory = _ => 8931,
        };
        var tokenOpt = new Option<string?>("--token")
        {
            Description =
                "Bearer token required on every HTTP request (or set " + TokenEnvVar + "). "
                + "Required when binding a non-loopback host.",
        };

        var cmd = new Command("mcp", "Run the Motus MCP server for agent clients (stdio by default, or --http)")
        {
            headlessOpt,
            channelOpt,
            httpOpt,
            hostOpt,
            portOpt,
            tokenOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var headless = parseResult.GetValue(headlessOpt);
            var channelText = parseResult.GetValue(channelOpt)!;
            var useHttp = parseResult.GetValue(httpOpt);

            // Prefer a browser installed via `motus install`; if none is found,
            // leave the path unset so the framework auto-detects a system browser.
            var executablePath = BrowserPathHelper.Resolve(channelText);

            var launchOptions = new McpServerLaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
            };

            if (!useHttp)
            {
                await McpServerHost.RunAsync(launchOptions, ct);
                return 0;
            }

            var token = parseResult.GetValue(tokenOpt)
                ?? Environment.GetEnvironmentVariable(TokenEnvVar);

            var httpOptions = new McpHttpServerOptions
            {
                Host = parseResult.GetValue(hostOpt)!,
                Port = parseResult.GetValue(portOpt),
                Token = string.IsNullOrEmpty(token) ? null : token,
                LaunchOptions = launchOptions,
            };

            try
            {
                await McpHttpServerHost.StartAsync(httpOptions, ct);
                return 0;
            }
            catch (InvalidOperationException ex)
            {
                // The host refuses to bind a non-loopback address without a token; surface the
                // reason instead of a stack trace.
                await Console.Error.WriteLineAsync(ex.Message);
                return 1;
            }
        });

        return cmd;
    }
}
