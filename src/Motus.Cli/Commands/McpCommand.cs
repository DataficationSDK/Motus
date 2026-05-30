using System.CommandLine;
using Motus.Mcp;

namespace Motus.Cli.Commands;

public static class McpCommand
{
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

        var cmd = new Command("mcp", "Run the Motus MCP server over stdio for agent clients")
        {
            headlessOpt,
            channelOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var headless = parseResult.GetValue(headlessOpt);
            var channelText = parseResult.GetValue(channelOpt)!;

            // Prefer a browser installed via `motus install`; if none is found,
            // leave the path unset so the framework auto-detects a system browser.
            var executablePath = BrowserPathHelper.Resolve(channelText);

            var options = new McpServerLaunchOptions
            {
                Headless = headless,
                ExecutablePath = executablePath,
            };

            await McpServerHost.RunAsync(options, ct);
            return 0;
        });

        return cmd;
    }
}
