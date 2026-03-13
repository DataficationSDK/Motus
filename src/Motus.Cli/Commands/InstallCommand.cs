using System.CommandLine;
using Motus.Cli.Services;

namespace Motus.Cli.Commands;

public static class InstallCommand
{
    public static Command Build()
    {
        var channelOpt = new Option<string>("--channel") { Description = "Browser channel to install (chromium, chrome, edge)", DefaultValueFactory = _ => "chromium" };
        var revisionOpt = new Option<string?>("--revision") { Description = "Pin to a specific browser revision" };
        var pathOpt = new Option<string?>("--path") { Description = "Override browser cache directory" };

        var cmd = new Command("install", "Download and install a browser for automation")
        {
            channelOpt,
            revisionOpt,
            pathOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var channel = parseResult.GetValue(channelOpt)!;
            var revision = parseResult.GetValue(revisionOpt);
            var cachePath = parseResult.GetValue(pathOpt);

            var installer = new BrowserInstaller();
            await installer.InstallAsync(channel, revision, cachePath);
        });

        return cmd;
    }
}
