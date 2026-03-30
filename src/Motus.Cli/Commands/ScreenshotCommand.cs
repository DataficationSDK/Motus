using System.CommandLine;
using Motus.Abstractions;

namespace Motus.Cli.Commands;

public static class ScreenshotCommand
{
    public static Command Build()
    {
        var urlArg = new Argument<string>("url") { Description = "URL to capture" };
        var outputOpt = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "screenshot.png" };
        var fullPageOpt = new Option<bool>("--full-page") { Description = "Capture the full scrollable page" };
        var widthOpt = new Option<int>("--width") { Description = "Viewport width in pixels", DefaultValueFactory = _ => 1280 };
        var heightOpt = new Option<int>("--height") { Description = "Viewport height in pixels", DefaultValueFactory = _ => 720 };
        var timeoutOpt = new Option<int>("--timeout") { Description = "Navigation timeout in seconds", DefaultValueFactory = _ => 60 };
        var waitUntilOpt = new Option<WaitUntil>("--wait-until") { Description = "Wait condition: Load, DOMContentLoaded, NetworkIdle", DefaultValueFactory = _ => WaitUntil.Load };
        var delayOpt = new Option<int>("--delay") { Description = "Seconds to wait after navigation before capture", DefaultValueFactory = _ => 0 };
        var hideBannersOpt = new Option<bool>("--hide-banners") { Description = "Remove cookie consent and privacy banners before capture" };

        var cmd = new Command("screenshot", "Capture a screenshot of a web page")
        {
            urlArg,
            outputOpt,
            fullPageOpt,
            widthOpt,
            heightOpt,
            timeoutOpt,
            waitUntilOpt,
            delayOpt,
            hideBannersOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var url = parseResult.GetValue(urlArg)!;
            var output = parseResult.GetValue(outputOpt)!;
            var fullPage = parseResult.GetValue(fullPageOpt);
            var width = parseResult.GetValue(widthOpt);
            var height = parseResult.GetValue(heightOpt);
            var timeoutMs = parseResult.GetValue(timeoutOpt) * 1000;
            var waitUntil = parseResult.GetValue(waitUntilOpt);

            var options = new LaunchOptions { Headless = true, ExecutablePath = BrowserPathHelper.Resolve() };
            var browser = await MotusLauncher.LaunchAsync(options, ct);
            try
            {
                var ctx = await browser.NewContextAsync();
                var page = await ctx.NewPageAsync();
                await page.SetViewportSizeAsync(new ViewportSize(width, height));
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

                await page.ScreenshotAsync(new ScreenshotOptions { Path = output, FullPage = fullPage });
                Console.WriteLine($"Screenshot saved to {output}");
            }
            finally
            {
                await browser.CloseAsync();
            }
        });

        return cmd;
    }
}
