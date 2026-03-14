using System.CommandLine;
using Motus.Cli.Services;
using Motus.Cli.Services.Reporters;
using Motus.Runner;

namespace Motus.Cli.Commands;

public static class RunCommand
{
    public static Command Build()
    {
        var assembliesArg = new Argument<string[]>("assemblies")
        {
            Description = "Test assembly paths",
            Arity = ArgumentArity.ZeroOrMore,
        };
        var filterOpt = new Option<string?>("--filter") { Description = "Filter tests by name substring" };
        var reporterOpt = new Option<string[]>("--reporter")
        {
            Description = "Reporter format (console, junit:path.xml, html:path.html)",
            Arity = ArgumentArity.ZeroOrMore,
            DefaultValueFactory = _ => new[] { "console" },
        };
        var workersOpt = new Option<string>("--workers") { Description = "Number of parallel workers (or 'auto')", DefaultValueFactory = _ => "auto" };
        var visualOpt = new Option<bool>("--visual") { Description = "Launch visual test runner" };

        var cmd = new Command("run", "Discover and execute tests from assemblies")
        {
            assembliesArg,
            filterOpt,
            reporterOpt,
            workersOpt,
            visualOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var assemblies = parseResult.GetValue(assembliesArg)!;
            var filter = parseResult.GetValue(filterOpt);
            var reporterSpecs = parseResult.GetValue(reporterOpt)!;
            var workersSpec = parseResult.GetValue(workersOpt)!;
            var visual = parseResult.GetValue(visualOpt);

            if (visual)
            {
                await RunnerHost.StartAsync([], assemblies, filter, port: 5100, ct: ct);
                return;
            }

            if (assemblies.Length == 0)
            {
                Console.Error.WriteLine("Error: At least one test assembly path is required.");
                return;
            }

            var workers = workersSpec.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? Environment.ProcessorCount
                : int.Parse(workersSpec);

            var reporter = ReporterFactory.Create(reporterSpecs);
            var discovery = new TestDiscovery();
            var tests = discovery.Discover(assemblies, filter);

            var runner = new TestRunner(workers);
            await runner.RunAsync(tests, reporter);
        });

        return cmd;
    }
}
