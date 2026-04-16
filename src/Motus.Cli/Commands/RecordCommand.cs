using System.CommandLine;
using Motus;
using Motus.Abstractions;
using Motus.Cli.Services;
using Motus.Recorder.ActionCapture;
using Motus.Recorder.CodeEmit;
using Motus.Selectors;

namespace Motus.Cli.Commands;

public static class RecordCommand
{
    public static Command Build()
    {
        var urlOpt = new Option<string?>("--url") { Description = "Starting URL to navigate to" };
        var outputOpt = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "recorded-test.cs" };
        var frameworkOpt = new Option<string>("--framework") { Description = "Test framework (mstest, xunit, nunit)", DefaultValueFactory = _ => "mstest" };
        var connectOpt = new Option<string?>("--connect") { Description = "WebSocket endpoint to connect to an existing browser" };
        var selectorPriorityOpt = new Option<string?>("--selector-priority") { Description = "Selector priority strategy (reserved for future use)" };
        var classNameOpt = new Option<string>("--class-name") { Description = "Generated test class name", DefaultValueFactory = _ => "RecordedTest" };
        var methodNameOpt = new Option<string>("--method-name") { Description = "Generated test method name", DefaultValueFactory = _ => "RecordedScenario" };
        var namespaceOpt = new Option<string>("--namespace") { Description = "Generated test namespace", DefaultValueFactory = _ => "Motus.Generated" };
        var preserveTimingOpt = new Option<bool>("--preserve-timing") { Description = "Emit delays between actions matching the original user timing" };
        var widthOpt = new Option<int>("--width") { Description = "Viewport width in pixels", DefaultValueFactory = _ => 1024 };
        var heightOpt = new Option<int>("--height") { Description = "Viewport height in pixels", DefaultValueFactory = _ => 768 };

        var cmd = new Command("record", "Record browser interactions and generate test code")
        {
            urlOpt,
            outputOpt,
            frameworkOpt,
            connectOpt,
            selectorPriorityOpt,
            classNameOpt,
            methodNameOpt,
            namespaceOpt,
            preserveTimingOpt,
            widthOpt,
            heightOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var url = parseResult.GetValue(urlOpt);
            var output = parseResult.GetValue(outputOpt)!;
            var framework = parseResult.GetValue(frameworkOpt)!;
            var connect = parseResult.GetValue(connectOpt);
            var className = parseResult.GetValue(classNameOpt)!;
            var methodName = parseResult.GetValue(methodNameOpt)!;
            var ns = parseResult.GetValue(namespaceOpt)!;
            var preserveTiming = parseResult.GetValue(preserveTimingOpt);
            var width = parseResult.GetValue(widthOpt);
            var height = parseResult.GetValue(heightOpt);

            IBrowser browser;
            if (connect is not null)
            {
                browser = await MotusLauncher.ConnectAsync(connect, ct);
            }
            else
            {
                var launchOptions = new LaunchOptions
                {
                    Headless = false,
                    ExecutablePath = BrowserPathHelper.Resolve(),
                    HandleSIGINT = false,
                };
                browser = await MotusLauncher.LaunchAsync(launchOptions, ct);
            }

            try
            {
                var page = await browser.NewPageAsync(new ContextOptions
                {
                    Viewport = new ViewportSize(width, height),
                });
                var engine = new ActionCaptureEngine();
                await engine.StartAsync(page, ct);

                if (url is not null)
                {
                    await page.GotoAsync(url);
                }

                Console.WriteLine("Recording... Press Enter to stop and generate test code.");

                await Task.Run(() => Console.ReadLine(), ct);

                await engine.StopAsync();

                var emitOptions = new CodeEmitOptions
                {
                    Framework = framework,
                    TestClassName = className,
                    TestMethodName = methodName,
                    Namespace = ns,
                    PreserveTiming = preserveTiming,
                };

                var emitter = new CodeEmitter();
                var emitResult = emitter.EmitWithMetadata(engine.CapturedActions, emitOptions);
                await File.WriteAllTextAsync(output, emitResult.Source, ct);
                Console.WriteLine($"Test code saved to {output}");

                var manifest = await BuildManifestAsync(page, Path.GetFullPath(output), emitResult.Locators, ct);
                var manifestPath = SelectorManifestWriter.ManifestPathFor(output);
                await SelectorManifestWriter.WriteAsync(manifest, manifestPath, ct);
                Console.WriteLine($"Selector manifest saved to {manifestPath} ({manifest.Entries.Count} entries)");
            }
            finally
            {
                await browser.CloseAsync();
            }
        });

        return cmd;
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
}
