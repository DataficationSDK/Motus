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
        var viewportOpt = new Option<string?>("--viewport")
        {
            Description = "Viewport size for every page as WIDTHxHEIGHT, e.g. 1920x1080 (default 1280x800)",
        };
        var recordVideoOpt = new Option<string?>("--record-video")
        {
            Description = "Record a video of every page into this directory (MJPEG AVI, one file per page)",
        };
        var showCursorOpt = new Option<bool>("--show-cursor")
        {
            Description = "Draw an on-screen pseudo-cursor in screenshots and recordings (follows the "
                + "element cursor style and shows a click effect); enables natural mouse motion "
                + "unless --natural-mouse is set explicitly",
            DefaultValueFactory = _ => false,
        };
        var naturalMouseOpt = new Option<bool?>("--natural-mouse")
        {
            Description = "Humanize mouse movement with curved, eased paths. Defaults to the value of "
                + "--show-cursor; pass --natural-mouse false to keep the cursor without it",
        };

        var cmd = new Command("mcp", "Run the Motus MCP server for agent clients (stdio by default, or --http)")
        {
            headlessOpt,
            channelOpt,
            httpOpt,
            hostOpt,
            portOpt,
            tokenOpt,
            viewportOpt,
            recordVideoOpt,
            showCursorOpt,
            naturalMouseOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var headless = parseResult.GetValue(headlessOpt);
            var channelText = parseResult.GetValue(channelOpt)!;
            var useHttp = parseResult.GetValue(httpOpt);

            Motus.Abstractions.ViewportSize? viewport = null;
            var viewportText = parseResult.GetValue(viewportOpt);
            if (viewportText is not null)
            {
                viewport = ParseViewport(viewportText);
                if (viewport is null)
                {
                    await Console.Error.WriteLineAsync(
                        $"Invalid --viewport value '{viewportText}'. Expected WIDTHxHEIGHT, e.g. 1280x800.");
                    return 1;
                }
            }

            // Prefer a browser installed via `motus install`; if none is found,
            // leave the path unset so the framework auto-detects a system browser.
            var executablePath = BrowserPathHelper.Resolve(channelText);

            // Natural motion defaults to the cursor flag so a single --show-cursor gives a
            // legible, human-looking capture; --natural-mouse overrides either way.
            var showCursor = parseResult.GetValue(showCursorOpt);
            var naturalMouse = parseResult.GetValue(naturalMouseOpt) ?? showCursor;

            var defaults = new McpServerLaunchOptions();
            var launchOptions = new McpServerLaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
                Viewport = viewport ?? defaults.Viewport,
                RecordVideoDir = parseResult.GetValue(recordVideoOpt),
                ShowCursor = showCursor,
                NaturalMouseMotion = naturalMouse,
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

    private static Motus.Abstractions.ViewportSize? ParseViewport(string text)
    {
        var parts = text.Split(['x', 'X'], 2);
        if (parts.Length == 2
            && int.TryParse(parts[0], out var width) && width > 0
            && int.TryParse(parts[1], out var height) && height > 0)
        {
            return new Motus.Abstractions.ViewportSize(width, height);
        }

        return null;
    }
}
