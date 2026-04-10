using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using CliTestResult = Motus.Cli.Services.TestResult;

namespace Motus.Cli.Services;

public sealed record TestResult(string FullName, bool Passed, TimeSpan Duration, string? ErrorMessage, string? StackTrace);

public sealed record TestRunResult(int Total, int Passed, int Failed, int Skipped, TimeSpan Duration);

public sealed class TestRunner(int maxWorkers)
{
    private readonly ConcurrentDictionary<Assembly, bool> _initializedAssemblies = new();
    private readonly ConcurrentDictionary<Type, bool> _initializedClasses = new();

    public Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, IReporter reporter) =>
        RunAsync(tests, reporter, a11yMode: null, enforcePerfBudget: false);

    public async Task<TestRunResult> RunAsync(List<DiscoveredTest> tests, IReporter reporter, string? a11yMode, bool enforcePerfBudget = false)
    {
        var suiteName = tests.Count > 0
            ? tests[0].TestClass.Assembly.GetName().Name ?? "Motus Tests"
            : "Motus Tests";

        await reporter.OnTestRunStartAsync(new TestSuiteInfo(suiteName, tests.Count));

        var sw = Stopwatch.StartNew();

        // Run [AssemblyInitialize] for each assembly represented in this batch
        var failedAssemblies = new HashSet<Assembly>();
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
                    failedAssemblies.Add(assembly);
                    var msg = ex.InnerException?.Message ?? ex.Message;
                    Console.Error.WriteLine($"AssemblyInitialize failed for {assembly.GetName().Name}: {msg}");
                }
            }
        }

        var semaphore = new SemaphoreSlim(maxWorkers);
        var results = new List<CliTestResult>();
        var lockObj = new object();

        var tasks = tests.Select(async test =>
        {
            // Skip ignored tests entirely — don't fire reporter events
            if (test.IsIgnored)
                return;

            await semaphore.WaitAsync();
            try
            {
                var testInfo = new TestInfo(test.FullName, suiteName);
                await reporter.OnTestStartAsync(testInfo);

                AccessibilityViolationSink.Begin();
                PerformanceMetricsSink.Begin();

                CliTestResult result;

                if (failedAssemblies.Contains(test.TestClass.Assembly))
                {
                    result = new CliTestResult(test.FullName, false, TimeSpan.Zero,
                        "AssemblyInitialize failed", null);
                }
                else
                {
                    result = await ExecuteTestAsync(test);
                }

                var violations = AccessibilityViolationSink.End();
                var perfMetrics = PerformanceMetricsSink.End();

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

                lock (lockObj)
                {
                    results.Add(result);
                }

                var absResult = new Abstractions.TestResult(
                    result.FullName, result.Passed, result.Duration.TotalMilliseconds,
                    result.ErrorMessage, result.StackTrace);
                await reporter.OnTestEndAsync(testInfo, absResult);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        sw.Stop();

        // Run [ClassCleanup] for all initialized classes
        foreach (var type in _initializedClasses.Keys)
        {
            try
            {
                await RunStaticLifecycleMethodAsync(type, "ClassCleanupAttribute");
                await RunStaticLifecycleMethodAsync(type, "OneTimeTearDownAttribute");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ClassCleanup failed for {type.FullName}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // Run [AssemblyCleanup] for each initialized assembly
        foreach (var assembly in assemblies)
        {
            try
            {
                await RunAssemblyCleanupAsync(assembly);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"AssemblyCleanup failed for {assembly.GetName().Name}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        var skipped = tests.Count(t => t.IsIgnored);
        var passed = results.Count(r => r.Passed);
        var failed = results.Count(r => !r.Passed);
        var runResult = new TestRunResult(passed + failed + skipped, passed, failed, skipped, sw.Elapsed);

        await reporter.OnTestRunEndAsync(new TestRunSummary(suiteName, passed, failed, skipped, sw.Elapsed.TotalMilliseconds));
        return runResult;
    }

    private async Task<CliTestResult> ExecuteTestAsync(DiscoveredTest test)
    {
        var testSw = Stopwatch.StartNew();
        object? instance = null;
        try
        {
            instance = Activator.CreateInstance(test.TestClass)!;

            // Run [ClassInitialize] once per type (static, takes TestContext)
            if (_initializedClasses.TryAdd(test.TestClass, true))
            {
                await RunStaticLifecycleMethodAsync(test.TestClass, "ClassInitializeAttribute");
                await RunStaticLifecycleMethodAsync(test.TestClass, "OneTimeSetUpAttribute");
            }

            // Run [TestInitialize] / [SetUp]
            await ExecuteLifecycleMethodAsync(instance, test.TestClass, "TestInitializeAttribute");
            await ExecuteLifecycleMethodAsync(instance, test.TestClass, "SetUpAttribute");

            var result = test.TestMethod.Invoke(instance, null);

            if (result is Task task)
                await task;

            testSw.Stop();
            return new CliTestResult(test.FullName, true, testSw.Elapsed, null, null);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            testSw.Stop();
            return new CliTestResult(test.FullName, false, testSw.Elapsed, ex.InnerException.Message, ex.InnerException.StackTrace);
        }
        catch (Exception ex)
        {
            testSw.Stop();
            return new CliTestResult(test.FullName, false, testSw.Elapsed, ex.Message, ex.StackTrace);
        }
        finally
        {
            if (instance is not null)
            {
                try
                {
                    // Run [TestCleanup] / [TearDown]
                    await ExecuteLifecycleMethodAsync(instance, test.TestClass, "TestCleanupAttribute");
                    await ExecuteLifecycleMethodAsync(instance, test.TestClass, "TearDownAttribute");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"TestCleanup failed for {test.FullName}: {ex.InnerException?.Message ?? ex.Message}");
                }

                if (instance is IAsyncDisposable asyncDisposable)
                    await asyncDisposable.DisposeAsync();
                else if (instance is IDisposable disposable)
                    disposable.Dispose();
            }
        }
    }

    private static async Task ExecuteLifecycleMethodAsync(object instance, Type testClass, string attributeName)
    {
        // Walk the hierarchy bottom-up, then reverse so base-class methods run first
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

    private static async Task RunAssemblyCleanupAsync(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == "AssemblyCleanupAttribute"));

            foreach (var method in methods)
            {
                var result = method.Invoke(null, null);
                if (result is Task task)
                    await task;
            }
        }
    }

    private static async Task RunAssemblyInitializeAsync(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == "AssemblyInitializeAttribute"));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                object?[] args = parameters.Length > 0 ? [null] : [];
                var result = method.Invoke(null, args);
                if (result is Task task)
                    await task;
            }
        }
    }

    private static async Task RunStaticLifecycleMethodAsync(Type type, string attributeName)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName));

        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            object?[] args = parameters.Length > 0 ? [null] : [];
            var result = method.Invoke(null, args);
            if (result is Task task)
                await task;
        }
    }
}
