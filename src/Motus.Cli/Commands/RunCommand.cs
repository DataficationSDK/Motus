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
            Description = "Reporter format: console | junit:<path> | html:<path> | trx:<path>. Repeat the flag for multiple reporters (e.g. --reporter console --reporter junit:r.xml).",
            Arity = ArgumentArity.ZeroOrMore,
            DefaultValueFactory = _ => new[] { "console" },
        };
        var workersOpt = new Option<string>("--workers") { Description = "Number of parallel workers (or 'auto')", DefaultValueFactory = _ => "auto" };
        var visualOpt = new Option<bool>("--visual") { Description = "Launch visual test runner" };
        var a11yOpt = new Option<string?>("--a11y") { Description = "Enable accessibility audits (warn or enforce)" };
        var perfBudgetOpt = new Option<bool>("--perf-budget") { Description = "Enable performance budget enforcement from config" };
        var coverageOpt = new Option<string[]?>("--coverage")
        {
            Description = "Enable coverage reporting: console | html:<dir> | cobertura:<path>. Repeat the flag for multiple formats (e.g. --coverage console --coverage html:./out).",
            Arity = ArgumentArity.ZeroOrMore,
        };

        var cmd = new Command("run", "Discover and execute tests from assemblies")
        {
            assembliesArg,
            filterOpt,
            reporterOpt,
            workersOpt,
            visualOpt,
            a11yOpt,
            perfBudgetOpt,
            coverageOpt,
        };

        cmd.SetAction(async (parseResult, ct) =>
        {
            var assemblies = parseResult.GetValue(assembliesArg)!;
            var filter = parseResult.GetValue(filterOpt);
            var reporterSpecs = parseResult.GetValue(reporterOpt)!;
            var workersSpec = parseResult.GetValue(workersOpt)!;
            var visual = parseResult.GetValue(visualOpt);
            var a11yMode = parseResult.GetValue(a11yOpt);
            var perfBudget = parseResult.GetValue(perfBudgetOpt);
            var coverageSpecs = parseResult.GetValue(coverageOpt);
            var coverageRequested = parseResult.GetResult(coverageOpt) is not null;

            if (a11yMode is not null)
            {
                Environment.SetEnvironmentVariable("MOTUS_ACCESSIBILITY_ENABLE", "true");
                Environment.SetEnvironmentVariable("MOTUS_ACCESSIBILITY_MODE", a11yMode);
            }

            if (perfBudget)
            {
                Environment.SetEnvironmentVariable("MOTUS_PERFORMANCE_ENABLE", "true");
            }

            if (coverageRequested)
            {
                Environment.SetEnvironmentVariable("MOTUS_COVERAGE_ENABLE", "true");
            }

            if (visual)
            {
                await RunnerHost.StartAsync([], assemblies, filter, port: 5100, ct: ct);
                return 0;
            }

            if (assemblies.Length == 0)
            {
                Console.Error.WriteLine("Error: At least one test assembly path is required.");
                return 1;
            }

            var workers = workersSpec.Equals("auto", StringComparison.OrdinalIgnoreCase)
                ? Environment.ProcessorCount
                : int.Parse(workersSpec);

            var reporter = ReporterFactory.Create(reporterSpecs);
            var coverageReporters = coverageRequested
                ? CoverageReporterFactory.Create(coverageSpecs)
                : null;

            var discovery = new TestDiscovery();
            var tests = discovery.Discover(assemblies, filter);

            var runner = new TestRunner(workers);
            var runResult = await runner.RunAsync(tests, reporter, a11yMode, perfBudget, coverageReporters);

            return (runResult.Failed > 0 || runResult.CoverageThresholdsFailed) ? 1 : 0;
        });

        return cmd;
    }
}
