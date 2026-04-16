using System.CommandLine;
using Motus;
using Motus.Abstractions;
using Motus.Cli.Services;
using Motus.Recorder.CodeEmit;
using Motus.Recorder.PageAnalysis;
using Motus.Recorder.PomEmit;
using Motus.Selectors;

namespace Motus.Cli.Commands;

public static class CodegenCommand
{
    public static Command Build()
    {
        var urlArg = new Argument<string[]>("url")
        {
            Description = "One or more URLs to generate page objects for (optional with --connect)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var outputOpt = new Option<string>("--output")
        {
            Description = "Output directory for generated files",
            DefaultValueFactory = _ => "."
        };

        var namespaceOpt = new Option<string>("--namespace")
        {
            Description = "Namespace for generated classes",
            DefaultValueFactory = _ => "Motus.Generated"
        };

        var selectorPriorityOpt = new Option<string?>("--selector-priority")
        {
            Description = "Comma-separated selector strategy priority (e.g. testid,role,text,css)"
        };

        var timeoutOpt = new Option<double>("--timeout")
        {
            Description = "Navigation timeout in milliseconds",
            DefaultValueFactory = _ => 30_000
        };

        var detectListenersOpt = new Option<bool>("--detect-listeners")
        {
            Description = "Detect elements with JS event listeners (for vanilla JS, jQuery, etc.)"
        };

        var connectOpt = new Option<string?>("--connect")
        {
            Description = "WebSocket endpoint to connect to an existing browser (e.g. ws://localhost:9222)"
        };

        var headedOpt = new Option<bool>("--headed")
        {
            Description = "Launch a visible browser so you can navigate and interact before analysis"
        };

        var scopeOpt = new Option<string?>("--scope")
        {
            Description = "CSS selector to limit discovery to a specific container (e.g. \".modal-dialog\", \"#login-form\")"
        };

        var cmd = new Command("codegen", "Generate page object models from live web pages")
        {
            urlArg,
            outputOpt,
            namespaceOpt,
            selectorPriorityOpt,
            timeoutOpt,
            detectListenersOpt,
            connectOpt,
            headedOpt,
            scopeOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var urls = parseResult.GetValue(urlArg) ?? [];
            var outputDir = parseResult.GetValue(outputOpt)!;
            var ns = parseResult.GetValue(namespaceOpt)!;
            var selectorPriorityRaw = parseResult.GetValue(selectorPriorityOpt);
            var timeoutMs = parseResult.GetValue(timeoutOpt);
            var connect = parseResult.GetValue(connectOpt);
            var headed = parseResult.GetValue(headedOpt);

            IReadOnlyList<string>? selectorPriority = null;
            if (!string.IsNullOrWhiteSpace(selectorPriorityRaw))
            {
                selectorPriority = selectorPriorityRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
            }

            if (urls.Length == 0 && connect is null && !headed)
            {
                Console.Error.WriteLine("Error: Provide at least one URL, use --connect, or use --headed.");
                return;
            }

            Directory.CreateDirectory(outputDir);

            var analysisOptions = new PageAnalysisOptions
            {
                SelectorPriority = selectorPriority,
                DetectEventListeners = parseResult.GetValue(detectListenersOpt),
                Scope = parseResult.GetValue(scopeOpt),
            };

            var emitter = new PomEmitter();
            var isConnected = connect is not null;

            IBrowser browser;
            if (isConnected)
            {
                browser = await MotusLauncher.ConnectAsync(connect!, ct);
            }
            else
            {
                var launchOptions = new LaunchOptions
                {
                    Headless = !headed,
                    ExecutablePath = BrowserPathHelper.Resolve(),
                };
                browser = await MotusLauncher.LaunchAsync(launchOptions, ct);
            }

            try
            {
                if (headed && urls.Length == 0)
                {
                    // Headed mode with no URLs: open a blank page and let the user navigate
                    await browser.NewPageAsync();

                    await PromptAndAnalyzeAsync(browser, analysisOptions, emitter, ns, outputDir, ct);
                }
                else if (headed)
                {
                    // Headed mode with URLs: navigate, let the user interact, then analyze
                    var page = await browser.NewPageAsync();

                    foreach (var url in urls)
                    {
                        await page.GotoAsync(url, new NavigationOptions { Timeout = (int)timeoutMs });
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, timeoutMs);

                        await PromptAndAnalyzeAsync(browser, analysisOptions, emitter, ns, outputDir, ct);
                    }
                }
                else if (isConnected && urls.Length == 0)
                {
                    // Connect mode: analyze the active page
                    var page = GetActivePage(browser);
                    if (page is null)
                    {
                        Console.Error.WriteLine("Error: No open pages found in the connected browser.");
                        return;
                    }

                    await AnalyzePageAsync(page, page.Url, analysisOptions, emitter, ns, outputDir, ct);
                }
                else
                {
                    // Standard mode: headless navigation to each URL
                    IPage page;
                    if (isConnected)
                    {
                        page = GetActivePage(browser) ?? await browser.NewPageAsync();
                    }
                    else
                    {
                        var ctx = await browser.NewContextAsync();
                        page = await ctx.NewPageAsync();
                    }

                    foreach (var url in urls)
                    {
                        Console.WriteLine($"Analyzing {url}...");
                        await page.GotoAsync(url, new NavigationOptions { Timeout = (int)timeoutMs });
                        await page.WaitForLoadStateAsync(LoadState.NetworkIdle, timeoutMs);

                        await AnalyzePageAsync(page, url, analysisOptions, emitter, ns, outputDir, ct);
                    }
                }
            }
            finally
            {
                if (!isConnected)
                    await browser.CloseAsync();
            }
        });

        return cmd;
    }

    private static async Task PromptAndAnalyzeAsync(
        IBrowser browser, PageAnalysisOptions analysisOptions, PomEmitter emitter,
        string ns, string outputDir, CancellationToken ct)
    {
        Console.WriteLine("Navigate to the page you want to analyze in the browser.");
        Console.WriteLine("Press Enter when ready...");
        await Task.Run(() => Console.ReadLine(), ct);

        // Re-resolve the active page since the user may have opened new tabs
        // or closed the original one while navigating.
        var page = GetActivePage(browser);
        if (page is null)
        {
            Console.Error.WriteLine("Error: No open pages found in the browser.");
            return;
        }

        var url = page.Url;
        if (string.IsNullOrEmpty(url) || url == "about:blank")
        {
            Console.Error.WriteLine("Error: No page loaded. Navigate to a page before pressing Enter.");
            return;
        }

        await AnalyzePageAsync(page, url, analysisOptions, emitter, ns, outputDir, ct);
    }

    private static async Task AnalyzePageAsync(
        IPage page, string url, PageAnalysisOptions analysisOptions, PomEmitter emitter,
        string ns, string outputDir, CancellationToken ct)
    {
        Console.WriteLine($"Analyzing {url}...");
        var engine = PageAnalysisEngine.Create(page, analysisOptions);
        var elements = await engine.AnalyzeAsync(page, ct);
        var className = PageClassNameDeriver.Derive(url);

        var options = new PomEmitOptions { Namespace = ns, ClassName = className, PageUrl = url };
        var emitResult = emitter.EmitWithMetadata(elements, options);
        var filePath = Path.Combine(outputDir, $"{className}.g.cs");
        await File.WriteAllTextAsync(filePath, emitResult.Source, ct);
        Console.WriteLine($"  Generated {filePath} ({elements.Count} elements)");

        var manifest = await BuildManifestAsync(page, Path.GetFullPath(filePath), emitResult.Locators, ct);
        var manifestPath = SelectorManifestWriter.ManifestPathFor(filePath);
        await SelectorManifestWriter.WriteAsync(manifest, manifestPath, ct);
        Console.WriteLine($"  Generated {manifestPath} ({manifest.Entries.Count} manifest entries)");
    }

    private static async Task<SelectorManifest> BuildManifestAsync(
        IPage page, string sourceFile, IReadOnlyList<EmittedLocator> locators, CancellationToken ct)
    {
        var session = ((Page)page).Session;
        var entries = new List<SelectorEntry>(locators.Count);

        foreach (var locator in locators)
        {
            DomFingerprint? fingerprint = null;
            if (locator.BackendNodeId is int id)
            {
                fingerprint = await DomFingerprintBuilder.TryBuildAsync(session, id, ct);
            }

            entries.Add(new SelectorEntry(
                Selector: locator.Selector,
                LocatorMethod: locator.LocatorMethod,
                SourceFile: sourceFile,
                SourceLine: locator.SourceLine,
                PageUrl: locator.PageUrl,
                Fingerprint: fingerprint ?? EmptyFingerprint()));
        }

        return new SelectorManifest(entries);
    }

    private static DomFingerprint EmptyFingerprint()
        => new(
            TagName: string.Empty,
            KeyAttributes: new Dictionary<string, string>(),
            VisibleText: null,
            AncestorPath: string.Empty,
            Hash: string.Empty);

    private static IPage? GetActivePage(IBrowser browser)
    {
        foreach (var ctx in browser.Contexts)
        {
            if (ctx.Pages.Count > 0)
                return ctx.Pages[^1];
        }
        return null;
    }
}
