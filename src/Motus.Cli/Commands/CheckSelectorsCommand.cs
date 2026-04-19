using System.CommandLine;
using Motus.Cli.Services;

namespace Motus.Cli.Commands;

public static class CheckSelectorsCommand
{
    public static Command Build()
    {
        var globArg = new Argument<string>("glob")
        {
            Description = "Glob pattern for C# source files to scan (e.g. \"./Tests/**/*.cs\")"
        };

        var manifestOpt = new Option<string?>("--manifest")
        {
            Description = "Path to a *.selectors.json manifest. When provided, selectors are checked against their recorded PageUrl."
        };

        var baseUrlOpt = new Option<string?>("--base-url")
        {
            Description = "Base URL to check every selector against. Required when --manifest is not provided."
        };

        var ciOpt = new Option<bool>("--ci")
        {
            Description = "Exit with a non-zero status code if any selector is broken."
        };

        var jsonOpt = new Option<string?>("--json")
        {
            Description = "Write the full check results as JSON to the given path."
        };

        var fixOpt = new Option<bool>("--fix")
        {
            Description = "Apply High-confidence repair suggestions to source files."
        };

        var noBackupOpt = new Option<bool>("--no-backup")
        {
            Description = "Do not write .bak files when applying fixes."
        };

        var interactiveOpt = new Option<bool>("--interactive")
        {
            Description = "Open the visual runner to review and apply repairs one selector at a time."
        };

        var cmd = new Command("check-selectors", "Validate recorded selectors against live pages")
        {
            globArg,
            manifestOpt,
            baseUrlOpt,
            ciOpt,
            jsonOpt,
            fixOpt,
            noBackupOpt,
            interactiveOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var glob = parseResult.GetValue(globArg)!;
            var manifest = parseResult.GetValue(manifestOpt);
            var baseUrl = parseResult.GetValue(baseUrlOpt);
            var ci = parseResult.GetValue(ciOpt);
            var jsonPath = parseResult.GetValue(jsonOpt);
            var fix = parseResult.GetValue(fixOpt);
            var noBackup = parseResult.GetValue(noBackupOpt);
            var interactive = parseResult.GetValue(interactiveOpt);

            if (manifest is null && baseUrl is null)
            {
                Console.Error.WriteLine("error: either --manifest or --base-url must be provided.");
                return 2;
            }

            if (fix && manifest is null)
            {
                Console.Error.WriteLine("error: --fix requires --manifest (fingerprint match is required for High-confidence repairs).");
                return 2;
            }

            if (interactive && manifest is null)
            {
                Console.Error.WriteLine("error: --interactive requires --manifest (fingerprint match is required to surface repair suggestions).");
                return 2;
            }

            if (interactive && fix)
            {
                Console.Error.WriteLine("error: --interactive and --fix cannot be combined.");
                return 2;
            }

            var runner = new CheckSelectorsRunner();
            if (interactive)
                return await runner.RunInteractiveAsync(glob, manifest!, ci, jsonPath, backup: !noBackup, ct);

            return await runner.RunAsync(glob, manifest, baseUrl, ci, jsonPath, fix, backup: !noBackup, ct);
        });

        return cmd;
    }
}
