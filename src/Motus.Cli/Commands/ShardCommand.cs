using System.CommandLine;
using Motus.Cli.Services;

namespace Motus.Cli.Commands;

public static class ShardCommand
{
    public static Command Build()
    {
        var filesArg = new Argument<string[]>("files")
        {
            Description = "Per-shard result files to merge (JUnit or TRX). A glob like results.shard-*.xml is accepted if the shell did not expand it.",
            Arity = ArgumentArity.OneOrMore,
        };
        var outputOpt = new Option<string[]>("--output")
        {
            Description = "Write the merged report: junit:<path> | trx:<path>. Repeat the flag for multiple formats. Omit to print only the console summary.",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var expectOpt = new Option<int?>("--expect")
        {
            Description = "Assert exactly N shards are present, read from the coordinates each shard stamped into its file. Fails the merge if a shard is missing or duplicated.",
        };

        var mergeCmd = new Command("merge", "Merge per-shard result files into one aggregate report")
        {
            filesArg,
            outputOpt,
            expectOpt,
        };

        mergeCmd.SetAction(async (parseResult, ct) =>
        {
            var files = parseResult.GetValue(filesArg)!;
            var outputs = parseResult.GetValue(outputOpt) ?? [];
            var expect = parseResult.GetValue(expectOpt);

            var inputs = ShardMerger.ExpandInputs(files);
            if (inputs.Count == 0)
            {
                Console.Error.WriteLine("Error: No input result files found.");
                return 1;
            }

            ShardMergeResult result;
            try
            {
                result = await ShardMerger.MergeAsync(inputs, outputs, expect);
            }
            catch (Exception ex) when (ex is IOException or FormatException or ArgumentException or FileNotFoundException)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
                return 1;
            }

            foreach (var error in result.Errors)
                Console.Error.WriteLine($"Error: {error}");

            Console.WriteLine(
                $"Merged {result.FileCount} shard file(s): {result.Passed} passed, {result.Failed} failed, " +
                $"{result.Skipped} skipped, {result.Flaky} flaky, {result.Quarantined} quarantined.");

            return result.Success ? 0 : 1;
        });

        return new Command("shard", "Test sharding commands")
        {
            mergeCmd,
        };
    }
}
