using System.CommandLine;
using Motus.Cli.Services;

namespace Motus.Cli.Commands;

public static class UpdateProtocolCommand
{
    public static Command Build()
    {
        var versionOpt = new Option<string?>("--version") { Description = "Protocol version (default: latest)" };
        var dryRunOpt = new Option<bool>("--dry-run") { Description = "Show diff without writing files" };
        var outputDirOpt = new Option<string>("--output-dir") { Description = "Directory to write protocol files", DefaultValueFactory = _ => "." };

        var cmd = new Command("update-protocol", "Download and update CDP protocol definitions")
        {
            versionOpt,
            dryRunOpt,
            outputDirOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var version = parseResult.GetValue(versionOpt);
            var dryRun = parseResult.GetValue(dryRunOpt);
            var outputDir = parseResult.GetValue(outputDirOpt)!;

            var updater = new ProtocolUpdater();
            await updater.UpdateAsync(version, dryRun, outputDir);
        });

        return cmd;
    }
}
