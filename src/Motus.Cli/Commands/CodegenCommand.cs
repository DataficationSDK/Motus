using System.CommandLine;

namespace Motus.Cli.Commands;

public static class CodegenCommand
{
    public static Command Build()
    {
        var cmd = new Command("codegen", "Generate code from page interactions (Phase 3E)");

        cmd.SetAction((parseResult) =>
        {
            Console.WriteLine("Not yet implemented.");
        });

        return cmd;
    }
}
