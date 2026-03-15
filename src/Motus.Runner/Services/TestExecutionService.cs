using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Motus;
using Motus.Abstractions;
using Motus.Runner.Services.Models;

namespace Motus.Runner.Services;

public sealed class TestExecutionService(ILogger<TestExecutionService> logger)
{
    // Browser automation tests must run sequentially to avoid contention
    // on the shared browser process and screenshot capture pipeline
    private const int MaxWorkers = 1;

    // Track which assemblies have had [AssemblyInitialize] run
    private readonly ConcurrentDictionary<Assembly, bool> _initializedAssemblies = new();

    public async Task ExecuteAsync(
        List<DiscoveredTest> tests,
        Action<TestNodeState> onStateChanged,
        CancellationToken ct,
        ReporterCollection? reporters = null)
    {
        var suiteName = tests.Count > 0
            ? tests[0].TestClass.Assembly.GetName().Name ?? "Motus Tests"
            : "Motus Tests";

        if (reporters is not null)
            await reporters.FireOnTestRunStartAsync(new TestSuiteInfo(suiteName, tests.Count));

        // Run [AssemblyInitialize] for each assembly represented in this batch
        var assemblies = tests.Select(t => t.TestClass.Assembly).Distinct().ToList();
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
                    foreach (var test in tests.Where(t => t.TestClass.Assembly == assembly))
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

                var result = await ExecuteTestAsync(test, logger);
                onStateChanged(result);

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
    }

    private static async Task ExecuteLifecycleMethodAsync(object instance, Type testClass, string attributeName)
    {
        // Walk the hierarchy bottom-up, then reverse so base-class methods run first.
        // GetMethods with FlattenHierarchy does not guarantee ordering, and running a
        // derived [TestInitialize] before the base (e.g. MotusTestBase.MotusTestInitialize)
        // causes null-reference failures when the derived method accesses Page/Context.
        var methods = new List<MethodInfo>();
        var type = testClass;
        while (type is not null && type != typeof(object))
        {
            var declared = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName));
            methods.InsertRange(0, declared);
            type = type.BaseType;
        }

        foreach (var method in methods)
        {
            var result = method.Invoke(instance, null);
            if (result is Task task)
                await task;
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
            {
                var parameters = method.GetParameters();
                // MSTest [AssemblyInitialize] takes a TestContext parameter
                object?[] args = parameters.Length > 0 ? [null] : [];
                var result = method.Invoke(null, args);
                if (result is Task task)
                    await task;
            }
        }
    }

    private static async Task<TestNodeState> ExecuteTestAsync(DiscoveredTest test, ILogger logger)
    {
        var sw = Stopwatch.StartNew();
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(test.TestClass)!;

            // Run [TestInitialize] methods
            await ExecuteLifecycleMethodAsync(instance, test.TestClass, "TestInitializeAttribute");
            // Also support NUnit [SetUp]
            await ExecuteLifecycleMethodAsync(instance, test.TestClass, "SetUpAttribute");

            var result = test.TestMethod.Invoke(instance, null);

            if (result is Task task)
                await task;

            sw.Stop();
            return new TestNodeState(test.FullName, TestStatus.Passed, sw.Elapsed, null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            sw.Stop();
            return new TestNodeState(test.FullName, TestStatus.Failed, sw.Elapsed, ex.InnerException.Message, ex.InnerException.StackTrace);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new TestNodeState(test.FullName, TestStatus.Failed, sw.Elapsed, ex.Message, ex.StackTrace);
        }
        finally
        {
            if (instance is not null)
            {
                try
                {
                    // Run [TestCleanup] methods
                    await ExecuteLifecycleMethodAsync(instance, test.TestClass, "TestCleanupAttribute");
                    // Also support NUnit [TearDown]
                    await ExecuteLifecycleMethodAsync(instance, test.TestClass, "TearDownAttribute");
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "TestCleanup failed for {Test}", test.FullName);
                }

                if (instance is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (instance is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }
}
