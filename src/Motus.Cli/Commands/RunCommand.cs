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
        var retriesOpt = new Option<int>("--retries")
        {
            Description = "Re-run a failing test up to N additional times. Which failures are retried depends on --retry-policy. Default: 0 (no retries).",
            DefaultValueFactory = _ => -1,
        };
        var retryPolicyOpt = new Option<string?>("--retry-policy")
        {
            Description = "Which failures --retries re-runs: 'transient' (only CDP disconnects, the default) or 'flake' (any failure; a test that then passes is reported as flaky). Flake detection needs --retries >= 1.",
        };
        var failOnFlakyOpt = new Option<bool?>("--fail-on-flaky")
        {
            Description = "Make the run exit non-zero when any test is flaky. By default flaky tests pass the run with a warning.",
        };
        var quarantineOpt = new Option<string?>("--quarantine")
        {
            Description = "Path to a quarantine list file (one fully qualified test name per line; '#' comments allowed). Listed tests run but their failures do not gate the run.",
        };
        var flakyHistoryOpt = new Option<string?>("--flaky-history")
        {
            Description = "Path to a JSON file that accumulates per-test run/failure/flaky-pass counts across runs.",
        };
        var shardOpt = new Option<string?>("--shard")
        {
            Description = "Run only one shard of the suite: <index>/<total>, 1-based (e.g. --shard 1/4). Discovered tests are partitioned deterministically across shards so independent runs (one per CI agent) cover disjoint subsets. Write each shard to its own result file (e.g. results.shard-1.xml) and combine them with 'motus shard merge'.",
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
            retriesOpt,
            retryPolicyOpt,
            failOnFlakyOpt,
            quarantineOpt,
            flakyHistoryOpt,
            shardOpt,
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

            // Flaky settings resolve CLI > env/file config > built-in default.
            var flakyConfig = Motus.MotusConfigLoader.Config.Flaky;

            var retriesCli = parseResult.GetValue(retriesOpt);
            var retries = retriesCli >= 0 ? retriesCli : (flakyConfig?.Retries ?? 0);

            var retryPolicySpec = parseResult.GetValue(retryPolicyOpt) ?? flakyConfig?.RetryPolicy ?? "transient";
            if (!TryParseRetryPolicy(retryPolicySpec, out var retryPolicy))
            {
                Console.Error.WriteLine($"Error: Unknown --retry-policy '{retryPolicySpec}'. Use 'transient' or 'flake'.");
                return 1;
            }

            var failOnFlaky = parseResult.GetValue(failOnFlakyOpt) ?? flakyConfig?.FailOnFlaky ?? false;
            var quarantinePath = parseResult.GetValue(quarantineOpt) ?? flakyConfig?.QuarantinePath;
            var flakyHistoryPath = parseResult.GetValue(flakyHistoryOpt) ?? flakyConfig?.HistoryPath;

            // Shard coordinates resolve CLI > env/file config. The CLI form is a single
            // "index/total" spec; config carries the two values separately.
            int? shardIndex = null;
            int? shardTotal = null;
            var shardSpec = parseResult.GetValue(shardOpt);
            if (shardSpec is not null)
            {
                if (!ShardSelector.TryParse(shardSpec, out var idx, out var tot, out var shardError))
                {
                    Console.Error.WriteLine($"Error: {shardError}");
                    return 1;
                }
                shardIndex = idx;
                shardTotal = tot;
            }
            else
            {
                var shardConfig = Motus.MotusConfigLoader.Config.Shard;
                if (shardConfig?.Index is int cfgIndex && shardConfig.Total is int cfgTotal)
                {
                    if (cfgTotal < 1 || cfgIndex < 1 || cfgIndex > cfgTotal)
                    {
                        Console.Error.WriteLine(
                            $"Error: Invalid shard config index/total {cfgIndex}/{cfgTotal}. The total must be at least 1 and the index between 1 and the total.");
                        return 1;
                    }
                    shardIndex = cfgIndex;
                    shardTotal = cfgTotal;
                }
            }

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

            var reporters = new List<Motus.Abstractions.IReporter> { ReporterFactory.Create(reporterSpecs) };
            if (flakyHistoryPath is not null)
                reporters.Add(new FlakeHistoryReporter(flakyHistoryPath));
            var reporter = reporters.Count == 1 ? reporters[0] : new CompositeReporter(reporters);

            IReadOnlyList<Motus.Abstractions.ICoverageReporter>? coverageReporters = null;
            if (coverageRequested)
            {
                try
                {
                    coverageReporters = CoverageReporterFactory.Create(coverageSpecs);
                }
                catch (ArgumentException ex)
                {
                    // Surface bad --coverage specs as a clean error, not a stack trace.
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 1;
                }
            }

            var discovery = new TestDiscovery();
            var tests = discovery.Discover(assemblies, filter);

            if (quarantinePath is not null)
            {
                HashSet<string> quarantineList;
                try
                {
                    quarantineList = LoadQuarantineList(quarantinePath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Error: Could not read quarantine file '{quarantinePath}': {ex.Message}");
                    return 1;
                }

                tests = tests
                    .Select(t => t with { Quarantined = t.Quarantined || quarantineList.Contains(t.FullName) })
                    .ToList();
            }

            // Partition to a single shard after discovery, filtering, and quarantine tagging so
            // each agent runs a disjoint subset. A shard is just a smaller test list; everything
            // downstream (workers, reporters, coverage, retries) is unchanged.
            if (shardIndex is int si && shardTotal is int st)
            {
                tests = ShardSelector.Select(tests, si, st);
                Console.WriteLine($"Shard {si}/{st}: running {tests.Count} test(s).");
            }

            var runner = new TestRunner(workers);
            var runResult = await runner.RunAsync(
                tests, reporter, a11yMode, perfBudget, coverageReporters, retries, retryPolicy,
                shardIndex, shardTotal);

            return (runResult.Failed > 0
                || runResult.CoverageThresholdsFailed
                || (failOnFlaky && runResult.Flaky > 0)) ? 1 : 0;
        });

        return cmd;
    }

    private static bool TryParseRetryPolicy(string spec, out RetryPolicy policy)
    {
        switch (spec.Trim().ToLowerInvariant())
        {
            case "transient":
                policy = RetryPolicy.Transient;
                return true;
            case "flake":
                policy = RetryPolicy.Flake;
                return true;
            default:
                policy = RetryPolicy.Transient;
                return false;
        }
    }

    private static HashSet<string> LoadQuarantineList(string path)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            set.Add(line);
        }
        return set;
    }
}
