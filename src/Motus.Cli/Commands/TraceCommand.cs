using System.CommandLine;
using Motus.Runner;

namespace Motus.Cli.Commands;

public static class TraceCommand
{
    public static Command Build()
    {
        var fileArg = new Argument<string>("file")
        {
            Description = "Path to a trace ZIP file",
        };
        var portOpt = new Option<int>("--port")
        {
            Description = "Port for the trace viewer",
            DefaultValueFactory = _ => 5200,
        };

        var showCmd = new Command("show", "Display a recorded trace in the visual runner")
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
                Console.Error.WriteLine($"Error: Trace file not found: {file}");
                return;
            }

            Console.WriteLine($"Loading trace from {file}...");
            await RunnerHost.StartAsync(
                [],
                traceFilePath: file,
                port: port,
                ct: ct);
        });

        var cmd = new Command("trace", "Trace management commands")
        {
            showCmd,
        };

        return cmd;
    }
}
