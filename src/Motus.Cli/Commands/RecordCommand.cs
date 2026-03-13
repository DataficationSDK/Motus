using System.CommandLine;
using Motus.Abstractions;
using Motus.Recorder.ActionCapture;
using Motus.Recorder.CodeEmit;

namespace Motus.Cli.Commands;

public static class RecordCommand
{
    public static Command Build()
    {
        var outputOpt = new Option<string>("--output") { Description = "Output file path", DefaultValueFactory = _ => "recorded-test.cs" };
        var frameworkOpt = new Option<string>("--framework") { Description = "Test framework (mstest, xunit, nunit)", DefaultValueFactory = _ => "mstest" };
        var connectOpt = new Option<string?>("--connect") { Description = "WebSocket endpoint to connect to an existing browser" };
        var selectorPriorityOpt = new Option<string?>("--selector-priority") { Description = "Selector priority strategy (reserved for future use)" };
        var classNameOpt = new Option<string>("--class-name") { Description = "Generated test class name", DefaultValueFactory = _ => "RecordedTest" };
        var methodNameOpt = new Option<string>("--method-name") { Description = "Generated test method name", DefaultValueFactory = _ => "RecordedScenario" };
        var namespaceOpt = new Option<string>("--namespace") { Description = "Generated test namespace", DefaultValueFactory = _ => "Motus.Generated" };

        var cmd = new Command("record", "Record browser interactions and generate test code")
        {
            outputOpt,
            frameworkOpt,
            connectOpt,
            selectorPriorityOpt,
            classNameOpt,
            methodNameOpt,
            namespaceOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var output = parseResult.GetValue(outputOpt)!;
            var framework = parseResult.GetValue(frameworkOpt)!;
            var connect = parseResult.GetValue(connectOpt);
            var className = parseResult.GetValue(classNameOpt)!;
            var methodName = parseResult.GetValue(methodNameOpt)!;
            var ns = parseResult.GetValue(namespaceOpt)!;

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
                };
                browser = await MotusLauncher.LaunchAsync(launchOptions, ct);
            }

            try
            {
                var page = await browser.NewPageAsync();
                var engine = new ActionCaptureEngine();
                await engine.StartAsync(page, ct);

                Console.WriteLine("Recording... Press Ctrl+C to stop.");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                Console.CancelKeyPress += (_, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                };

                try
                {
                    await Task.Delay(Timeout.Infinite, cts.Token);
                }
                catch (OperationCanceledException)
                {
                }

                await engine.StopAsync();

                var emitOptions = new CodeEmitOptions
                {
                    Framework = framework,
                    TestClassName = className,
                    TestMethodName = methodName,
                    Namespace = ns,
                };

                var emitter = new CodeEmitter();
                var code = emitter.Emit(engine.CapturedActions, emitOptions);
                await File.WriteAllTextAsync(output, code, ct);
                Console.WriteLine($"Test code saved to {output}");
            }
            finally
            {
                await browser.CloseAsync();
            }
        });

        return cmd;
    }
}
