using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Motus;
using Motus.Abstractions;
using Motus.Runner.Services.Coverage;
using Motus.Runner.Services.Execution;
using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public sealed class TestExecutionService(ILogger<TestExecutionService> logger, ICoverageService? coverageService = null)
{
    // Browser automation tests must run sequentially to avoid contention
    // on the shared browser process and screenshot capture pipeline
    private const int MaxWorkers = 1;

    // Track which assemblies have had [AssemblyInitialize] run
    private readonly ConcurrentDictionary<Assembly, bool> _initializedAssemblies = new();

    // Track which classes have had [ClassInitialize] / [OneTimeSetUp] run in this batch
    private readonly ConcurrentDictionary<Type, bool> _initializedClasses = new();

    public async Task ExecuteAsync(
        List<DiscoveredTest> tests,
        Action<TestNodeState> onStateChanged,
        CancellationToken ct,
        ReporterCollection? reporters = null)
    {
        foreach (var test in tests)
        {
            if (test.TestClass is null || test.TestMethod is null)
                throw new InvalidOperationException(
                    $"Test '{test.FullName}' cannot be executed: missing reflection metadata (TRX-loaded tests are read-only).");
        }

        var suiteName = tests.Count > 0
            ? tests[0].TestClass!.Assembly.GetName().Name ?? "Motus Tests"
            : "Motus Tests";

        if (reporters is not null)
            await reporters.FireOnTestRunStartAsync(new TestSuiteInfo(suiteName, tests.Count));

        // Run [AssemblyInitialize] for each assembly represented in this batch
        var assemblies = tests.Select(t => t.TestClass!.Assembly).Distinct().ToList();
        foreach (var assembly in assemblies)
        {
            if (_initializedAssemblies.TryAdd(assembly, true))
            {
                try
                {
                    await RunAssemblyInitializeAsync(assembly);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "AssemblyInitialize failed for {Assembly}", assembly.GetName().Name);
                    // Fail all tests in this assembly
                    foreach (var test in tests.Where(t => t.TestClass!.Assembly == assembly))
                    {
                        onStateChanged(new TestNodeState(test.FullName, TestStatus.Failed, null,
                            $"AssemblyInitialize failed: {ex.InnerException?.Message ?? ex.Message}",
                            ex.InnerException?.StackTrace ?? ex.StackTrace));
                    }
                    return;
                }
            }
        }

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

                CoverageSink.Begin();
                var result = await ExecuteTestAsync(test, ct);
                onStateChanged(result);

                var coverage = CoverageSink.End();
                if (coverage is not null)
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

        // Run [ClassCleanup] / [OneTimeTearDown] for each class initialized in this batch
        foreach (var type in _initializedClasses.Keys.ToList())
        {
            if (_initializedClasses.TryRemove(type, out _))
            {
                try
                {
                    await RunStaticLifecycleMethodsAsync(type, "ClassCleanupAttribute");
                    await RunStaticLifecycleMethodsAsync(type, "OneTimeTearDownAttribute");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "ClassCleanup failed for {Class}", type.FullName);
                }
            }
        }

        // Run [AssemblyCleanup] for each initialized assembly to close browsers
        foreach (var assembly in assemblies)
        {
            if (_initializedAssemblies.TryRemove(assembly, out _))
            {
                try
                {
                    await RunAssemblyCleanupAsync(assembly);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "AssemblyCleanup failed for {Assembly}", assembly.GetName().Name);
                }
            }
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

    private static async Task RunAssemblyInitializeAsync(Assembly assembly)
    {
        // [AssemblyInitialize] is a static method that takes TestContext
        // Look across all types in the assembly for it
        foreach (var type in assembly.GetExportedTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == "AssemblyInitializeAttribute"));

            foreach (var method in methods)
                await TestCaseInvoker.InvokeStaticAsync(method, type);
        }
    }

    private static async Task RunAssemblyCleanupAsync(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == "AssemblyCleanupAttribute"));

            foreach (var method in methods)
                await TestCaseInvoker.InvokeStaticAsync(method, type);
        }
    }

    private static async Task RunStaticLifecycleMethodsAsync(Type type, string attributeName)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName));

        foreach (var method in methods)
            await TestCaseInvoker.InvokeStaticAsync(method, type);
    }

    private async Task<TestNodeState> ExecuteTestAsync(DiscoveredTest test, CancellationToken ct)
    {
        var testClass = test.TestClass!;
        var run = await TestCaseInvoker.RunAsync(
            testClass,
            test.TestMethod!,
            test.Arguments,
            DisplayNameOf(test),
            beforeInstance: async () =>
            {
                // Run [ClassInitialize] / [OneTimeSetUp] once per class, before its first test
                if (_initializedClasses.TryAdd(testClass, true))
                {
                    await RunStaticLifecycleMethodsAsync(testClass, "ClassInitializeAttribute");
                    await RunStaticLifecycleMethodsAsync(testClass, "OneTimeSetUpAttribute");
                }
            },
            onCleanupFailure: ex => logger.LogWarning(ex, "TestCleanup failed for {Test}", test.FullName),
            cancellationToken: ct);

        return run.Failure is null
            ? new TestNodeState(test.FullName, TestStatus.Passed, run.Duration, null, null)
            : new TestNodeState(test.FullName, TestStatus.Failed, run.Duration, run.Failure.Message, run.Failure.StackTrace);
    }

    /// <summary>The test case's name without its class, such as <c>SignsIn ("owner")</c>.</summary>
    private static string DisplayNameOf(DiscoveredTest test)
    {
        var prefix = test.TestClass!.FullName + ".";
        return test.FullName.StartsWith(prefix, StringComparison.Ordinal) ? test.FullName[prefix.Length..] : test.TestMethod!.Name;
    }
}
