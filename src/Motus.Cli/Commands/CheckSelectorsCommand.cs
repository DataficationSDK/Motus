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

        var cmd = new Command("check-selectors", "Validate recorded selectors against live pages")
        {
            globArg,
            manifestOpt,
            baseUrlOpt,
            ciOpt,
            jsonOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var glob = parseResult.GetValue(globArg)!;
            var manifest = parseResult.GetValue(manifestOpt);
            var baseUrl = parseResult.GetValue(baseUrlOpt);
            var ci = parseResult.GetValue(ciOpt);
            var jsonPath = parseResult.GetValue(jsonOpt);

            if (manifest is null && baseUrl is null)
            {
                Console.Error.WriteLine("error: either --manifest or --base-url must be provided.");
                return 2;
            }

            var runner = new CheckSelectorsRunner();
            return await runner.RunAsync(glob, manifest, baseUrl, ci, jsonPath, ct);
        });

        return cmd;
    }
}
