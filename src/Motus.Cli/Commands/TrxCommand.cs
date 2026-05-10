using System.CommandLine;
using Motus.Runner;

namespace Motus.Cli.Commands;

public static class TrxCommand
{
    public static Command Build()
    {
        var fileArg = new Argument<string>("file")
        {
            Description = "Path to a .trx result file",
        };
        var portOpt = new Option<int>("--port")
        {
            Description = "Port for the TRX viewer",
            DefaultValueFactory = _ => 5300,
        };

        var showCmd = new Command("show", "Display TRX results in the visual runner")
        {
            fileArg,
            portOpt,
        };

        showCmd.SetAction(async (parseResult, ct) =>
        {
            var file = parseResult.GetValue(fileArg)!;
            var port = parseResult.GetValue(portOpt);

            if (!File.Exists(file))
            {
                Console.Error.WriteLine($"Error: TRX file not found: {file}");
                return;
            }

            if (!file.EndsWith(".trx", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine($"Error: file must have a .trx extension: {file}");
                return;
            }

            Console.WriteLine($"Loading TRX from {file}...");
            await RunnerHost.StartAsync(
                [],
                trxFilePath: file,
                port: port,
                ct: ct);
        });

        return new Command("trx", "TRX result commands")
        {
            showCmd,
        };
    }
}
