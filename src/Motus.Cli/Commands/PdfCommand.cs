using System.CommandLine;
using Motus.Abstractions;

namespace Motus.Cli.Commands;

public static class PdfCommand
{
    public static Command Build()
    {
        var urlArg = new Argument<string>("url") { Description = "URL to render as PDF" };
        var outputOpt = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "output.pdf" };

        var cmd = new Command("pdf", "Generate a PDF from a web page")
        {
            urlArg,
            outputOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var url = parseResult.GetValue(urlArg)!;
            var output = parseResult.GetValue(outputOpt)!;

            var options = new LaunchOptions { Headless = true, ExecutablePath = BrowserPathHelper.Resolve() };
            var browser = await MotusLauncher.LaunchAsync(options, ct);
            try
            {
                var ctx = await browser.NewContextAsync();
                var page = await ctx.NewPageAsync();
                await page.GotoAsync(url);
                await page.WaitForLoadStateAsync();
                await page.PdfAsync(output);
                Console.WriteLine($"PDF saved to {output}");
            }
            finally
            {
                await browser.CloseAsync();
            }
        });

        return cmd;
    }
}
