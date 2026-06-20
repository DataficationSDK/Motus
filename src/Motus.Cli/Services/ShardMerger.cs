using System.Globalization;
using System.Xml.Linq;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Services;

/// <summary>
/// Aggregate outcome of merging per-shard result files: summed counts, the number of files
/// merged, and whether shard-completeness validation passed. <see cref="Success"/> is the
/// combined gate used for the process exit code.
/// </summary>
public sealed record ShardMergeResult(
    int Passed,
    int Failed,
    int Skipped,
    int Flaky,
    int Quarantined,
    int FileCount,
    bool ValidationPassed,
    IReadOnlyList<string> Errors)
{
    public bool Success => Failed == 0 && ValidationPassed && FileCount > 0;
}

/// <summary>
/// Recombines the JUnit and/or TRX result files produced by independent shard runs into one
/// aggregate report. Operates on files, not live reporter state, because JUnit and TRX are the
/// cross-tool interchange formats Motus already writes. Optionally validates that the inputs
/// cover an expected number of shards by reading the coordinates each shard stamps into its file.
/// </summary>
public static class ShardMerger
{
    private static readonly XNamespace TrxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private sealed record ParsedFile(
        List<(TestInfo Info, Abstractions.TestResult Result)> Cases,
        int Skipped,
        (int Index, int Total)? Coords);

    /// <summary>
    /// Expands any input arguments containing a wildcard (in case the shell did not expand a
    /// glob like <c>results.shard-*.xml</c>) and drops duplicates while preserving order.
    /// </summary>
    public static List<string> ExpandInputs(IEnumerable<string> inputs)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();
        foreach (var input in inputs)
        {
            if (input.Contains('*') || input.Contains('?'))
            {
                var dir = Path.GetDirectoryName(input);
                var pattern = Path.GetFileName(input);
                var searchDir = string.IsNullOrEmpty(dir) ? "." : dir;
                if (Directory.Exists(searchDir))
                {
                    foreach (var match in Directory.EnumerateFiles(searchDir, pattern).OrderBy(p => p, StringComparer.Ordinal))
                    {
                        if (seen.Add(match))
                            result.Add(match);
                    }
                }
            }
            else if (seen.Add(input))
            {
                result.Add(input);
            }
        }
        return result;
    }

    /// <summary>
    /// Parses every input file, unions the cases, validates shard completeness against
    /// <paramref name="expect"/> when supplied, and writes the merged report to each
    /// <paramref name="outputs"/> spec (<c>junit:&lt;path&gt;</c> / <c>trx:&lt;path&gt;</c>) by
    /// replaying the cases through the existing reporter writers.
    /// </summary>
    public static async Task<ShardMergeResult> MergeAsync(
        IReadOnlyList<string> inputs,
        IReadOnlyList<string> outputs,
        int? expect)
    {
        var cases = new List<(TestInfo Info, Abstractions.TestResult Result)>();
        var perFileCoords = new List<(int Index, int Total)?>();
        var totalSkipped = 0;

        foreach (var path in inputs)
        {
            var parsed = ParseFile(path);
            cases.AddRange(parsed.Cases);
            totalSkipped += parsed.Skipped;
            perFileCoords.Add(parsed.Coords);
        }

        var passed = cases.Count(c => c.Result.Passed && !c.Result.Quarantined);
        var failed = cases.Count(c => !c.Result.Passed && !c.Result.Quarantined);
        var quarantined = cases.Count(c => c.Result.Quarantined);
        var flaky = cases.Count(c => c.Result.Flaky && !c.Result.Quarantined);
        var totalDurationMs = cases.Sum(c => c.Result.DurationMs);

        var (validationPassed, errors) = Validate(perFileCoords, expect);

        var summary = new TestRunSummary(
            "motus-merged", passed, failed, totalSkipped, totalDurationMs, flaky, quarantined);

        foreach (var spec in outputs)
        {
            var reporter = CreateOutputReporter(spec);
            await reporter.OnTestRunStartAsync(new TestSuiteInfo(summary.SuiteName, cases.Count));
            foreach (var (info, result) in cases)
            {
                await reporter.OnTestStartAsync(info);
                await reporter.OnTestEndAsync(info, result);
            }
            await reporter.OnTestRunEndAsync(summary);
        }

        return new ShardMergeResult(
            passed, failed, totalSkipped, flaky, quarantined, inputs.Count, validationPassed, errors);
    }

    private static (bool Passed, List<string> Errors) Validate(
        IReadOnlyList<(int Index, int Total)?> coords, int? expect)
    {
        var errors = new List<string>();
        if (expect is not int e)
            return (true, errors);

        if (coords.Any(c => c is null))
        {
            errors.Add("One or more shard files are missing shard coordinates; re-run those shards with --shard so merge can verify completeness.");
            return (false, errors);
        }

        var present = coords.Select(c => c!.Value).ToList();

        var totals = present.Select(c => c.Total).Distinct().OrderBy(t => t).ToList();
        if (totals.Count != 1 || totals[0] != e)
            errors.Add($"Expected {e} shards but the inputs declare total(s) {string.Join(", ", totals)}.");

        var indices = present.Select(c => c.Index).ToList();
        if (indices.Distinct().Count() != indices.Count)
            errors.Add($"Duplicate shard index among the inputs (indices seen: {string.Join(", ", indices.OrderBy(i => i))}).");

        var expectedSet = Enumerable.Range(1, e).ToHashSet();
        if (!indices.ToHashSet().SetEquals(expectedSet))
            errors.Add($"Shard indices {string.Join(", ", indices.Distinct().OrderBy(i => i))} do not cover the full set 1..{e}.");

        return (errors.Count == 0, errors);
    }

    private static IReporter CreateOutputReporter(string spec)
    {
        var colon = spec.IndexOf(':');
        if (colon < 0)
            throw new ArgumentException($"Invalid --output '{spec}'. Expected junit:<path> or trx:<path>.");

        var format = spec[..colon].ToLowerInvariant();
        var path = spec[(colon + 1)..];
        return format switch
        {
            "junit" => new JUnitReporter(path),
            "trx" => new TrxReporter(path),
            _ => throw new ArgumentException($"Unknown --output format '{format}'. Use junit or trx."),
        };
    }

    private static ParsedFile ParseFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Result file not found: {path}");

        var root = XDocument.Load(path).Root
            ?? throw new FormatException($"Empty result file: {path}");

        return root.Name.LocalName switch
        {
            "testsuites" or "testsuite" => ParseJUnit(root),
            "TestRun" => ParseTrx(root),
            _ => throw new FormatException($"Unrecognized result file format (root <{root.Name.LocalName}>): {path}"),
        };
    }

    private static ParsedFile ParseJUnit(XElement root)
    {
        var suites = root.Name.LocalName == "testsuite"
            ? new[] { root }
            : root.Elements().Where(e => e.Name.LocalName == "testsuite").ToArray();

        var cases = new List<(TestInfo, Abstractions.TestResult)>();
        var skipped = 0;
        (int, int)? coords = null;

        foreach (var suite in suites)
        {
            skipped += (int?)suite.Attribute("skipped") ?? 0;

            var props = suite.Element("properties");
            if (props is not null)
            {
                var idx = PropertyValue(props, "motus.shard.index");
                var tot = PropertyValue(props, "motus.shard.total");
                if (idx is int i && tot is int t)
                    coords = (i, t);
            }

            var suiteName = (string?)suite.Attribute("name") ?? "merged";

            foreach (var tc in suite.Elements("testcase"))
            {
                var name = (string?)tc.Attribute("name") ?? "";
                var classname = (string?)tc.Attribute("classname") ?? suiteName;
                var durationMs = ((double?)tc.Attribute("time") ?? 0) * 1000.0;

                var quarantineNote = tc.Elements("system-out")
                    .FirstOrDefault(e => e.Value.StartsWith("QUARANTINED", StringComparison.Ordinal));
                var primaryFailure = tc.Elements("failure")
                    .FirstOrDefault(f => (string?)f.Attribute("type") != "accessibility");
                var flakyFailure = tc.Element("flakyFailure");

                bool passed = true, flaky = false, quarantined = false;
                string? error = null, stack = null;
                var attempts = 1;

                if (quarantineNote is not null)
                {
                    quarantined = true;
                    passed = quarantineNote.Value.Contains("(passed", StringComparison.Ordinal);
                    if (!passed)
                        error = quarantineNote.Value;
                }
                else if (primaryFailure is not null)
                {
                    passed = false;
                    error = (string?)primaryFailure.Attribute("message");
                    stack = string.IsNullOrEmpty(primaryFailure.Value) ? null : primaryFailure.Value;
                }
                else if (flakyFailure is not null)
                {
                    flaky = true;
                    attempts = ParseAttempts((string?)flakyFailure.Attribute("message"));
                }

                cases.Add((
                    new TestInfo(name, classname),
                    new Abstractions.TestResult(name, passed, durationMs, error, stack,
                        Flaky: flaky, Quarantined: quarantined, Attempts: attempts)));
            }
        }

        return new ParsedFile(cases, skipped, coords);
    }

    private static ParsedFile ParseTrx(XElement root)
    {
        var cases = new List<(TestInfo, Abstractions.TestResult)>();
        var skipped = 0;
        (int, int)? coords = null;

        if ((int?)root.Attribute("motusShardIndex") is int idx
            && (int?)root.Attribute("motusShardTotal") is int tot)
            coords = (idx, tot);

        var counters = root.Element(TrxNs + "ResultSummary")?.Element(TrxNs + "Counters");
        if (counters is not null)
            skipped = (int?)counters.Attribute("notExecuted") ?? 0;

        // TestCategory lives on the UnitTest definitions, keyed by test name.
        var flakyNames = new HashSet<string>(StringComparer.Ordinal);
        var quarantineNames = new HashSet<string>(StringComparer.Ordinal);
        var definitions = root.Element(TrxNs + "TestDefinitions");
        if (definitions is not null)
        {
            foreach (var ut in definitions.Elements(TrxNs + "UnitTest"))
            {
                var name = (string?)ut.Attribute("name") ?? "";
                var categories = ut.Element(TrxNs + "TestCategory")?
                    .Elements(TrxNs + "TestCategoryItem")
                    .Select(c => (string?)c.Attribute("TestCategory"))
                    .ToHashSet() ?? new HashSet<string?>();
                if (categories.Contains("flaky")) flakyNames.Add(name);
                if (categories.Contains("quarantine")) quarantineNames.Add(name);
            }
        }

        var results = root.Element(TrxNs + "Results");
        if (results is not null)
        {
            foreach (var utr in results.Elements(TrxNs + "UnitTestResult"))
            {
                var name = (string?)utr.Attribute("testName") ?? "";
                var outcome = (string?)utr.Attribute("outcome") ?? "Passed";
                var durationMs = ParseTrxDuration((string?)utr.Attribute("duration"));

                var quarantined = outcome == "Inconclusive" || quarantineNames.Contains(name);
                // A quarantined test reports as Inconclusive, so its underlying pass/fail is not
                // preserved; treat it as passed for replay since quarantined never gates anyway.
                var passed = quarantined || outcome == "Passed";
                var flaky = !quarantined && flakyNames.Contains(name);

                string? error = null, stack = null;
                if (!passed)
                {
                    var errorInfo = utr.Element(TrxNs + "Output")?.Element(TrxNs + "ErrorInfo");
                    error = errorInfo?.Element(TrxNs + "Message")?.Value;
                    stack = errorInfo?.Element(TrxNs + "StackTrace")?.Value;
                }

                var lastDot = name.LastIndexOf('.');
                var classname = lastDot > 0 ? name[..lastDot] : name;

                cases.Add((
                    new TestInfo(name, classname),
                    new Abstractions.TestResult(name, passed, durationMs, error, stack,
                        Flaky: flaky, Quarantined: quarantined, Attempts: flaky ? 2 : 1)));
            }
        }

        return new ParsedFile(cases, skipped, coords);
    }

    private static int? PropertyValue(XElement properties, string name)
    {
        var prop = properties.Elements("property")
            .FirstOrDefault(p => (string?)p.Attribute("name") == name);
        return prop is null ? null : (int?)prop.Attribute("value");
    }

    private static int ParseAttempts(string? message)
    {
        // Message shape: "passed after N attempts".
        if (message is null)
            return 2;
        var digits = new string(message.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) && n > 0 ? n : 2;
    }

    private static double ParseTrxDuration(string? duration) =>
        TimeSpan.TryParse(duration, CultureInfo.InvariantCulture, out var ts) ? ts.TotalMilliseconds : 0;
}
