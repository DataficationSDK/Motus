using System.CommandLine;
using Motus.Cli.Commands;

var root = new RootCommand("Motus CLI - Browser automation toolkit")
{
    RunCommand.Build(),
    RecordCommand.Build(),
    CodegenCommand.Build(),
    InstallCommand.Build(),
    UpdateProtocolCommand.Build(),
    ScreenshotCommand.Build(),
    PdfCommand.Build(),
    TraceCommand.Build(),
    CheckSelectorsCommand.Build(),
};

var config = new CommandLineConfiguration(root);
return await config.InvokeAsync(args);
