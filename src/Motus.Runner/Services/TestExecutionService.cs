using System.Diagnostics;
using Motus.Abstractions;
using Motus.Runner.Services.Coverage;
using Motus.Runner.Services.Execution;
using Motus.Runner.Services.Models;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services;

/// <summary>
/// Runs tests for the visual runner. Each run starts a test host process for each test assembly
/// it covers, which streams the browser and the timeline back here, and ends it when the run is
/// over. Tests built from reflection metadata rather than discovered run in this process.
/// </summary>
public sealed class TestExecutionService(
    ILogger<TestExecutionService> logger,
    ICoverageService? coverageService = null,
    IScreencastService? screencast = null,
    ITimelineService? timeline = null,
    IStepDebugService? stepDebug = null)
{
    // Browser automation tests must run sequentially to avoid contention
    // on the shared browser process and screenshot capture pipeline
    private const int MaxWorkers = 1;

    public async Task ExecuteAsync(
        List<DiscoveredTest> tests,
        Action<TestNodeState> onStateChanged,
        CancellationToken ct,
        ReporterCollection? reporters = null)
    {
        foreach (var test in tests)
        {
            if ((test.TestClass is null || test.TestMethod is null) && string.IsNullOrEmpty(test.CodeBase))
                throw new InvalidOperationException(
                    $"Test '{test.FullName}' cannot be executed: missing reflection metadata (TRX-loaded tests are read-only).");
        }

        var suiteName = tests.Count > 0 ? tests[0].AssemblyName : "Motus Tests";

        if (reporters is not null)
            await reporters.FireOnTestRunStartAsync(new TestSuiteInfo(suiteName, tests.Count));

        var hosts = new Dictionary<string, ITestAssemblyHost>(StringComparer.Ordinal);
        try
        {
            // Start a test host for each assembly in this batch and run its [AssemblyInitialize]
            foreach (var group in tests.GroupBy(AssemblyKeyOf))
            {
                var first = group.First();
                ITestAssemblyHost host;
                try
                {
                    host = await CreateHostAsync(first, group, ct);
                }
                catch (TestHostException ex)
                {
                    logger.LogError("Could not start a test host for {Assembly}: {Message}", first.AssemblyName, ex.Message);
                    foreach (var test in group)
                        onStateChanged(new TestNodeState(test.FullName, TestStatus.Failed, null, ex.Message, null));
                    return;
                }
                catch (OperationCanceledException)
                {
                    // Stopped while the test host was starting.
                    foreach (var test in tests)
                        onStateChanged(new TestNodeState(test.FullName, TestStatus.Skipped, null, "Cancelled", null));
                    return;
                }

                hosts[group.Key] = host;

                var failure = await host.InitializeAsync(ct);
                if (failure is not null)
                {
                    logger.LogError("AssemblyInitialize failed for {Assembly}: {Message}", first.AssemblyName, failure.Message);
                    // Fail all tests in this assembly
                    foreach (var test in group)
                    {
                        onStateChanged(new TestNodeState(test.FullName, TestStatus.Failed, null,
                            $"AssemblyInitialize failed: {failure.Message}", failure.StackTrace));
                    }
                    return;
                }
            }

            await RunTestsAsync(tests, hosts, suiteName, onStateChanged, ct, reporters);

            // Run [ClassCleanup] / [OneTimeTearDown] for each class that ran a test, then
            // [AssemblyCleanup] to close browsers
            foreach (var host in hosts.Values)
                await host.CleanupAsync();
        }
        finally
        {
            foreach (var host in hosts.Values)
                await host.DisposeAsync();

            (screencast as ScreencastService)?.EndRemoteFrames();
        }
    }

    private async Task RunTestsAsync(
        List<DiscoveredTest> tests,
        Dictionary<string, ITestAssemblyHost> hosts,
        string suiteName,
        Action<TestNodeState> onStateChanged,
        CancellationToken ct,
        ReporterCollection? reporters)
    {
        var sw = reporters is not null ? Stopwatch.StartNew() : null;
        var semaphore = new SemaphoreSlim(MaxWorkers);
        int passedCount = 0, failedCount = 0, skippedCount = 0;
        var collectedCoverage = new List<CoverageData>();
        var coverageLock = new object();

        var tasks = tests.Select(async test =>
        {
            if (test.IsIgnored)
            {
                onStateChanged(new TestNodeState(test.FullName, TestStatus.Skipped, null, test.IgnoreReason ?? "Ignored", null));
                if (reporters is not null) Interlocked.Increment(ref skippedCount);
                return;
            }

            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();

                onStateChanged(new TestNodeState(test.FullName, TestStatus.Running, null, null, null));

                if (reporters is not null)
                    await reporters.FireOnTestStartAsync(new TestInfo(test.FullName, suiteName));

                var report = await hosts[AssemblyKeyOf(test)].RunAsync(test.FullName, ct);
                var result = report.Passed
                    ? new TestNodeState(test.FullName, TestStatus.Passed, report.Duration, null, null)
                    : new TestNodeState(test.FullName, TestStatus.Failed, report.Duration, report.ErrorMessage, report.StackTrace);
                onStateChanged(result);

                if (report.Coverage is { } coverage)
                {
                    lock (coverageLock)
                        collectedCoverage.Add(coverage);
                }

                if (reporters is not null)
                {
                    var passed = result.Status == TestStatus.Passed;
                    var durationMs = result.Duration?.TotalMilliseconds ?? 0;
                    var testInfo = new TestInfo(test.FullName, suiteName);
                    var testResult = new Abstractions.TestResult(
                        test.FullName, passed, durationMs,
                        result.ErrorMessage, result.StackTrace);
                    await reporters.FireOnTestEndAsync(testInfo, testResult);

                    if (result.Status == TestStatus.Passed) Interlocked.Increment(ref passedCount);
                    else if (result.Status == TestStatus.Failed) Interlocked.Increment(ref failedCount);
                    else Interlocked.Increment(ref skippedCount);
                }
            }
            catch (OperationCanceledException)
            {
                onStateChanged(new TestNodeState(test.FullName, TestStatus.Skipped, null, "Cancelled", null));
                if (reporters is not null) Interlocked.Increment(ref skippedCount);
            }
            finally
            {
                semaphore.Release();
            }
        });

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Test run cancelled");
        }

        if (reporters is not null)
        {
            sw!.Stop();
            await reporters.FireOnTestRunEndAsync(
                new TestRunSummary(suiteName, passedCount, failedCount, skippedCount, sw.Elapsed.TotalMilliseconds));
        }

        if (coverageService is not null && collectedCoverage.Count > 0)
        {
            var mergedScripts = CoverageAggregator.MergeScripts(collectedCoverage.SelectMany(c => c.Scripts));
            var mergedSheets = CoverageAggregator.MergeStylesheets(collectedCoverage.SelectMany(c => c.Stylesheets));
            var mergedOriginal = CoverageAggregator.MergeOriginalFiles(collectedCoverage.SelectMany(c => c.OriginalFiles));
            var summary = CoverageAggregator.BuildSummary(mergedScripts, mergedSheets);
            var diagnostic = collectedCoverage.Select(c => c.DiagnosticMessage).FirstOrDefault(d => !string.IsNullOrEmpty(d));

            coverageService.Set(new CoverageData(
                mergedScripts,
                mergedSheets,
                summary,
                DateTime.UtcNow,
                diagnostic,
                mergedOriginal));
        }
    }

    /// <summary>Groups the tests of one assembly: by the assembly itself in this process, by its path otherwise.</summary>
    private static string AssemblyKeyOf(DiscoveredTest test)
        => test.TestClass is not null && test.TestMethod is not null
            ? $"in-process:{test.TestClass.Assembly.FullName}"
            : Path.GetFullPath(test.CodeBase!);

    private async Task<ITestAssemblyHost> CreateHostAsync(DiscoveredTest first, IEnumerable<DiscoveredTest> tests, CancellationToken ct)
    {
        if (first.TestClass is not null && first.TestMethod is not null)
        {
            var cases = tests.Select(t => new DiscoveredTestCase(t.TestClass!, t.TestMethod!, t.FullName, t.Arguments, t.IsIgnored, t.IgnoreReason, Quarantined: false));
            return new InProcessTestHost(first.TestClass.Assembly, cases, message => logger.LogWarning("{Message}", message));
        }

        var remoteScreencast = screencast as ScreencastService;
        var host = await TestHostClient.StartAsync(first.CodeBase!, new TestHostOptions
        {
            Visual = true,
            OnLog = message => logger.LogWarning("{Message}", message),
            OnFrame = remoteScreencast is null ? null : remoteScreencast.ShowRemoteFrame,
            OnTimelineEntry = timeline is null ? null : entry =>
                timeline.AddEntry(entry with { Index = timeline.Entries.Count, TestName = timeline.CurrentTestName }),
            OnStep = stepDebug is null ? null : (action, selector) => stepDebug.WaitIfPausedAsync(action, selector, CancellationToken.None),
        }, ct);

        // A fresh process discovers again; the run names its tests, and one that is gone fails saying so.
        try
        {
            await host.DiscoverAsync(filter: null, ct);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }

        return host;
    }
}
