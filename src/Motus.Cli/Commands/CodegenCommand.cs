using System.CommandLine;
using Motus.Abstractions;
using Motus.Recorder.PageAnalysis;
using Motus.Recorder.PomEmit;

namespace Motus.Cli.Commands;

public static class CodegenCommand
{
    public static Command Build()
    {
        var urlArg = new Argument<string[]>("url")
        {
            Description = "One or more URLs to generate page objects for",
            Arity = ArgumentArity.OneOrMore
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

        var cmd = new Command("codegen", "Generate page object models from live web pages")
        {
            urlArg,
            outputOpt,
            namespaceOpt,
            selectorPriorityOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var urls = parseResult.GetValue(urlArg)!;
            var outputDir = parseResult.GetValue(outputOpt)!;
            var ns = parseResult.GetValue(namespaceOpt)!;
            var selectorPriorityRaw = parseResult.GetValue(selectorPriorityOpt);

            IReadOnlyList<string>? selectorPriority = null;
            if (!string.IsNullOrWhiteSpace(selectorPriorityRaw))
            {
                selectorPriority = selectorPriorityRaw
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray();
            }

            Directory.CreateDirectory(outputDir);

            var launchOptions = new LaunchOptions
            {
                Headless = true,
                ExecutablePath = BrowserPathHelper.Resolve()
            };

            var browser = await MotusLauncher.LaunchAsync(launchOptions, ct);
            try
            {
                var ctx = await browser.NewContextAsync();
                var page = await ctx.NewPageAsync();

                var analysisOptions = new PageAnalysisOptions
                {
                    SelectorPriority = selectorPriority,
                };

                var engine = PageAnalysisEngine.Create(page, analysisOptions);
                var emitter = new PomEmitter();

                foreach (var url in urls)
                {
                    Console.WriteLine($"Analyzing {url}...");
                    await page.GotoAsync(url);
                    await page.WaitForLoadStateAsync();

                    var elements = await engine.AnalyzeAsync(page, ct);
                    var className = PageClassNameDeriver.Derive(url);

                    var options = new PomEmitOptions
                    {
                        Namespace = ns,
                        ClassName = className,
                        PageUrl = url
                    };

                    var code = emitter.Emit(elements, options);
                    var filePath = Path.Combine(outputDir, $"{className}.g.cs");
                    await File.WriteAllTextAsync(filePath, code, ct);
                    Console.WriteLine($"  Generated {filePath} ({elements.Count} elements)");
                }
            }
            finally
            {
                await browser.CloseAsync();
            }
        });

        return cmd;
    }
}
