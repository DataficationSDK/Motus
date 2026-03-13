using System.CommandLine;

namespace Motus.Cli.Commands;

public static class TraceCommand
{
    public static Command Build()
    {
        var showCmd = new Command("show", "Display a recorded trace (Phase 5C)");
        showCmd.SetAction((parseResult) =>
        {
            Console.WriteLine("Not yet implemented.");
        });

        var cmd = new Command("trace", "Trace management commands")
        {
            showCmd,
        };

        return cmd;
    }
}
