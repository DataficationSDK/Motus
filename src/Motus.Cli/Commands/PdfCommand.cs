using System.CommandLine;
using Motus.Abstractions;

namespace Motus.Cli.Commands;

public static class PdfCommand
{
    public static Command Build()
    {
        var urlArg = new Argument<string>("url") { Description = "URL to render as PDF" };
        var outputOpt = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "output.pdf" };
        var timeoutOpt = new Option<int>("--timeout") { Description = "Navigation timeout in seconds", DefaultValueFactory = _ => 60 };
        var waitUntilOpt = new Option<WaitUntil>("--wait-until") { Description = "Wait condition: Load, DOMContentLoaded, NetworkIdle", DefaultValueFactory = _ => WaitUntil.Load };
        var widthOpt = new Option<int>("--width") { Description = "Viewport width in pixels", DefaultValueFactory = _ => 1440 };
        var delayOpt = new Option<int>("--delay") { Description = "Seconds to wait after navigation before capture", DefaultValueFactory = _ => 0 };
        var hideBannersOpt = new Option<bool>("--hide-banners") { Description = "Remove cookie consent and privacy banners before capture" };

        var cmd = new Command("pdf", "Generate a PDF from a web page")
        {
            urlArg,
            outputOpt,
            timeoutOpt,
            waitUntilOpt,
            widthOpt,
            delayOpt,
            hideBannersOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var url = parseResult.GetValue(urlArg)!;
            var output = parseResult.GetValue(outputOpt)!;
            var timeoutMs = parseResult.GetValue(timeoutOpt) * 1000;
            var waitUntil = parseResult.GetValue(waitUntilOpt);

            var options = new LaunchOptions { Headless = true, ExecutablePath = BrowserPathHelper.Resolve() };
            var browser = await MotusLauncher.LaunchAsync(options, ct);
            try
            {
                var width = parseResult.GetValue(widthOpt);
                var ctx = await browser.NewContextAsync();
                var page = await ctx.NewPageAsync();
                await page.SetViewportSizeAsync(new ViewportSize(width, 900));
                await page.GotoAsync(url, new NavigationOptions { Timeout = timeoutMs, WaitUntil = waitUntil });

                var delay = parseResult.GetValue(delayOpt);
                if (delay > 0)
                    await Task.Delay(delay * 1000, ct);

                if (parseResult.GetValue(hideBannersOpt))
                {
                    var removed = await BannerRemoval.RemoveAsync(page);
                    if (removed > 0)
                        Console.WriteLine($"Removed {removed} banner element(s).");
                }

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
