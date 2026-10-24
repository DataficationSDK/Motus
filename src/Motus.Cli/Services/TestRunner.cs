using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Motus;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using Motus.Runner.Services.Execution;
using CliTestResult = Motus.Cli.Services.TestResult;

namespace Motus.Cli.Services;

public sealed record TestResult(string FullName, bool Passed, TimeSpan Duration, string? ErrorMessage, string? StackTrace, bool Flaky = false, bool Quarantined = false, int Attempts = 1);

public sealed record TestRunResult(int Total, int Passed, int Failed, int Skipped, TimeSpan Duration, bool CoverageThresholdsFailed = false, int Flaky = 0, int Quarantined = 0);

/// <summary>
/// Controls which failures the runner re-runs under <c>--retries</c>.
/// <see cref="Transient"/> retries only transient CDP disconnects (the default, safe
/// behavior). <see cref="Flake"/> retries any failure and reports a test that fails then
/// passes within its retry budget as flaky rather than failed.
/// </summary>
public enum RetryPolicy
{
    Transient,
    Flake,
}

public sealed class TestRunner(int maxWorkers)
{
    // Tests built from reflection metadata rather than discovered run in this process, one host per assembly.
    private readonly ConcurrentDictionary<Assembly, InProcessTestHost> _inProcessHosts = new();

    public Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, IReporter reporter) =>
        RunAsync(tests, reporter, a11yMode: null, enforcePerfBudget: false, coverageReporters: null);

    public Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, IReporter reporter, string? a11yMode) =>
        RunAsync(tests, reporter, a11yMode, enforcePerfBudget: false, coverageReporters: null);

    public Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, IReporter reporter, string? a11yMode, bool enforcePerfBudget) =>
        RunAsync(tests, reporter, a11yMode, enforcePerfBudget, coverageReporters: null);

    public async Task<TestRunResult> RunAsync(
        List<DiscoveredTest> tests,
        IReporter reporter,
        string? a11yMode,
        bool enforcePerfBudget,
        IReadOnlyList<ICoverageReporter>? coverageReporters,
        int maxRetries = 0,
        RetryPolicy retryPolicy = RetryPolicy.Transient,
        int? shardIndex = null,
        int? shardTotal = null)
    {
        if (maxRetries < 0)
            maxRetries = 0;
        var suiteName = tests.Count > 0 && !string.IsNullOrEmpty(tests[0].AssemblyName)
            ? tests[0].AssemblyName
            : "Motus Tests";

        await reporter.OnTestRunStartAsync(
            new TestSuiteInfo(suiteName, tests.Count, ShardIndex: shardIndex, ShardTotal: shardTotal));

        var sw = Stopwatch.StartNew();

        // Run [AssemblyInitialize] for each assembly represented in this batch, in the test host
        // process that assembly runs in
        var failedHosts = new HashSet<ITestAssemblyHost>();
        var hosts = tests.Select(HostFor).Distinct().ToList();
        foreach (var host in hosts)
        {
            var failure = await host.InitializeAsync();
            if (failure is not null)
            {
                failedHosts.Add(host);
                var assemblyName = tests.First(t => HostFor(t) == host).AssemblyName;
                Console.Error.WriteLine($"AssemblyInitialize failed for {assemblyName}: {failure.Message}");
            }
        }

        var semaphore = new SemaphoreSlim(maxWorkers);
        var results = new List<CliTestResult>();
        var collectedCoverage = new List<CoverageData>();
        var lockObj = new object();

        var tasks = tests.Select(async test =>
        {
            // Skip ignored tests entirely, without firing reporter events
            if (test.IsIgnored)
                return;

            await semaphore.WaitAsync();
            try
            {
                var testInfo = new TestInfo(test.FullName, suiteName);
                await reporter.OnTestStartAsync(testInfo);

                CliTestResult result;
                var attempts = 1;
                TestCaseReport? report = null;

                if (failedHosts.Contains(HostFor(test)))
                {
                    result = new CliTestResult(test.FullName, false, TimeSpan.Zero,
                        "AssemblyInitialize failed", null);
                }
                else
                {
                    var totalAttempts = maxRetries + 1;
                    var attempt = 0;
                    while (true)
                    {
                        attempt++;

                        // Each call rebuilds the test instance (and its browser context),
                        // so every retry runs against clean state. What the failed attempts
                        // collected is discarded: only the last attempt's report is kept.
                        report = await ExecuteTestAsync(test);
                        result = new CliTestResult(test.FullName, report.Passed, report.Duration, report.ErrorMessage, report.StackTrace);

                        if (result.Passed || attempt >= totalAttempts || !ShouldRetry(report, retryPolicy))
                            break;

                        WriteRetryNotice(test.FullName, attempt + 1, totalAttempts, retryPolicy);
                    }

                    attempts = attempt;
                }

                var violations = TestCaseReportExtras.Violations(report);
                var perfMetrics = TestCaseReportExtras.PerformanceMetrics(report);
                var coverage = TestCaseReportExtras.Coverage(report);

                if (coverage is not null)
                {
                    lock (lockObj)
                    {
                        collectedCoverage.Add(coverage);
                    }

                    if (coverageReporters is not null)
                    {
                        foreach (var cr in coverageReporters)
                        {
                            try { await cr.OnCoverageCollectedAsync(coverage, testInfo); }
                            catch { }
                        }
                    }

                    if (reporter is ICoverageReporter testReporterCoverage)
                    {
                        try { await testReporterCoverage.OnCoverageCollectedAsync(coverage, testInfo); }
                        catch { }
                    }
                }

                // Dispatch violations to IAccessibilityReporter reporters
                if (violations.Count > 0 && reporter is IAccessibilityReporter a11yReporter)
                {
                    foreach (var violation in violations)
                    {
                        try { await a11yReporter.OnAccessibilityViolationAsync(violation, testInfo); }
                        catch { }
                    }
                }

                // Dispatch performance metrics to IPerformanceReporter reporters
                if (perfMetrics is not null)
                {
                    var budget = ConfigMerge.ToBudget(MotusConfigLoader.Config.Performance);
                    var budgetResult = budget?.Evaluate(perfMetrics);

                    if (reporter is IPerformanceReporter perfReporter)
                    {
                        try { await perfReporter.OnPerformanceMetricsCollectedAsync(perfMetrics, budgetResult, testInfo); }
                        catch { }
                    }

                    // In enforce mode, override a passing test to failed when budget is exceeded
                    if (result.Passed && enforcePerfBudget && budgetResult is { Passed: false })
                    {
                        var failedCount = budgetResult.Entries.Count(e => !e.Passed);
                        result = result with
                        {
                            Passed = false,
                            ErrorMessage = $"Performance budget exceeded: {failedCount} metric(s) over budget.",
                        };
                    }
                }

                // In enforce mode, override a passing test to failed when Error violations exist
                if (result.Passed
                    && string.Equals(a11yMode, "enforce", StringComparison.OrdinalIgnoreCase)
                    && violations.Any(v => v.Severity == AccessibilityViolationSeverity.Error))
                {
                    var count = violations.Count(v => v.Severity == AccessibilityViolationSeverity.Error);
                    result = result with
                    {
                        Passed = false,
                        ErrorMessage = $"Accessibility enforcement failed: {count} error-severity violation(s) detected.",
                    };
                }

                // Classify after any enforce override above: a test the body passed on a
                // later attempt but enforce then failed is a hard failure, not flaky.
                var flaky = result.Passed && attempts > 1;
                result = result with
                {
                    Flaky = flaky,
                    Quarantined = test.Quarantined,
                    Attempts = attempts,
                };

                lock (lockObj)
                {
                    results.Add(result);
                }

                var absResult = new Abstractions.TestResult(
                    result.FullName, result.Passed, result.Duration.TotalMilliseconds,
                    result.ErrorMessage, result.StackTrace,
                    Flaky: result.Flaky, Quarantined: result.Quarantined, Attempts: result.Attempts);
                await reporter.OnTestEndAsync(testInfo, absResult);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        // Aggregate coverage and dispatch run-end events
        CoverageData? aggregated = null;
        var coverageThresholdsFailed = false;
        if (collectedCoverage.Count > 0)
        {
            var mergedScripts = CoverageAggregator.MergeScripts(collectedCoverage.SelectMany(c => c.Scripts));
            var mergedSheets = CoverageAggregator.MergeStylesheets(collectedCoverage.SelectMany(c => c.Stylesheets));
            var mergedOriginal = CoverageAggregator.MergeOriginalFiles(collectedCoverage.SelectMany(c => c.OriginalFiles));
            var summary = CoverageAggregator.BuildSummary(mergedScripts, mergedSheets);
            var diagnostic = collectedCoverage.Select(c => c.DiagnosticMessage).FirstOrDefault(d => !string.IsNullOrEmpty(d));

            aggregated = new CoverageData(
                mergedScripts,
                mergedSheets,
                summary,
                DateTime.UtcNow,
                diagnostic,
                mergedOriginal);

            // Threshold enforcement: read from MotusConfig and force-fail when violated
            var coverageOptions = ConfigMerge.ToCoverageOptions(MotusConfigLoader.Config.Coverage);
            if (coverageOptions is not null)
            {
                var thresholdResult = CoverageThresholds.Evaluate(aggregated, coverageOptions);
                if (!thresholdResult.Passed)
                {
                    coverageThresholdsFailed = true;
                    foreach (var entry in thresholdResult.Failed)
                    {
                        Console.Error.WriteLine(
                            $"Coverage threshold failed: {entry.MetricName} {entry.Actual:F1}% < {entry.Threshold:F1}%");
                    }
                }
            }

            if (coverageReporters is not null)
            {
                foreach (var cr in coverageReporters)
                {
                    try { await cr.OnCoverageRunEndAsync(aggregated); }
                    catch { }
                }
            }

            if (reporter is ICoverageReporter testReporterCoverageEnd)
            {
                try { await testReporterCoverageEnd.OnCoverageRunEndAsync(aggregated); }
                catch { }
            }
        }

        // Run [ClassCleanup] for each class that ran a test, then [AssemblyCleanup], assembly by assembly
        foreach (var host in hosts)
            await host.CleanupAsync();

        var skipped = tests.Count(t => t.IsIgnored);
        // Quarantined results occupy their own bucket and are excluded from passed/failed
        // so a quarantined failure never gates the run.
        var quarantined = results.Count(r => r.Quarantined);
        var passed = results.Count(r => r.Passed && !r.Quarantined);
        var failed = results.Count(r => !r.Passed && !r.Quarantined);
        var flaky = results.Count(r => r.Flaky && !r.Quarantined);
        var runResult = new TestRunResult(
            passed + failed + skipped + quarantined, passed, failed, skipped, sw.Elapsed,
            coverageThresholdsFailed, flaky, quarantined);

        await reporter.OnTestRunEndAsync(new TestRunSummary(
            suiteName, passed, failed, skipped, sw.Elapsed.TotalMilliseconds, flaky, quarantined));
        return runResult;
    }

    /// <summary>
    /// Emits a [RETRY] notice in the same shape (and color, when stderr is a TTY)
    /// as the [PASS]/[FAIL] lines emitted by ConsoleReporter, so the retry log
    /// reads as part of the test stream instead of free-form debug output.
    /// </summary>
    private static void WriteRetryNotice(string testName, int attempt, int totalAttempts, RetryPolicy policy)
    {
        const string Yellow = "\x1b[33m";
        const string Gray = "\x1b[90m";
        const string Reset = "\x1b[0m";

        var reason = policy == RetryPolicy.Flake ? "failure" : "CDP disconnect";

        var useColor = !Console.IsErrorRedirected;
        if (useColor)
        {
            Console.Error.WriteLine(
                $"  {Yellow}[RETRY]{Reset} {testName} {Gray}({reason}, attempt {attempt}/{totalAttempts}){Reset}");
        }
        else
        {
            Console.Error.WriteLine(
                $"  [RETRY] {testName} ({reason}, attempt {attempt}/{totalAttempts})");
        }
    }

    /// <summary>
    /// Decides whether a failed attempt should be retried under the active policy.
    /// <see cref="RetryPolicy.Flake"/> retries any failure; <see cref="RetryPolicy.Transient"/>
    /// retries only transient CDP disconnects.
    /// </summary>
    private static bool ShouldRetry(TestCaseReport report, RetryPolicy policy)
    {
        if (report.Passed)
            return false;
        return policy == RetryPolicy.Flake || report.TransientFailure;
    }

    /// <summary>
    /// The host that runs <paramref name="test"/>: the test host process that discovered it, or for a
    /// test built from reflection metadata, one in this process for its assembly.
    /// </summary>
    private ITestAssemblyHost HostFor(DiscoveredTest test)
        => test.Host ?? _inProcessHosts.GetOrAdd(test.TestClass!.Assembly, assembly => new InProcessTestHost(assembly, log: Console.Error.WriteLine));

    private Task<TestCaseReport> ExecuteTestAsync(DiscoveredTest test)
    {
        if (test.Host is { } host)
            return host.RunAsync(test.TestId, test.FullName);

        var testCase = new DiscoveredTestCase(test.TestClass!, test.TestMethod!, test.FullName, test.Arguments, test.IsIgnored, null, test.Quarantined, test.TestId);
        return ((InProcessTestHost)HostFor(test)).RunAsync(testCase);
    }
}
