using System.Collections.Concurrent;
using System.Reflection;

namespace Motus.Runner.Services.Execution;

/// <summary>
/// Runs the tests of an assembly loaded into this process, by reflection. The test host process
/// runs one of these for the assembly it was started for, and the runners' own tests use one
/// directly.
/// </summary>
internal sealed class InProcessTestHost : ITestAssemblyHost
{
    private readonly Assembly _assembly;
    private readonly ConcurrentDictionary<string, DiscoveredTestCase> _cases = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Type, bool> _initializedClasses = new();
    private readonly Action<string> _log;
    private int _initialized;

    /// <param name="log">Told about cleanup methods that throw, which do not fail a test.</param>
    public InProcessTestHost(Assembly assembly, IEnumerable<DiscoveredTestCase>? cases = null, Action<string>? log = null)
    {
        _assembly = assembly;
        _log = log ?? (_ => { });
        if (cases is not null)
        {
            foreach (var testCase in cases)
                _cases[testCase.Id] = testCase;
        }
    }

    public async Task<TestHostFailure?> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
            return null;

        try
        {
            // [AssemblyInitialize] is a static method that takes TestContext
            // Look across all types in the assembly for it
            foreach (var type in _assembly.GetExportedTypes())
            {
                foreach (var method in StaticMethodsWith(type, "AssemblyInitializeAttribute", BindingFlags.Public | BindingFlags.Static))
                    await TestCaseInvoker.InvokeStaticAsync(method, type);
            }

            return null;
        }
        catch (Exception ex)
        {
            return new TestHostFailure(ex.InnerException?.Message ?? ex.Message, ex.InnerException?.StackTrace ?? ex.StackTrace);
        }
    }

    public Task<TestCaseReport> RunAsync(string testId, string fullName, CancellationToken cancellationToken = default)
    {
        if (_cases.TryGetValue(testId, out var testCase))
            return RunAsync(testCase, cancellationToken);

        // A data row's name can change from one read of its data source to the next, but a test
        // without data rows, or one whose id predates a rebuild, is still found by its name.
        testCase = _cases.Values.FirstOrDefault(c => c.FullName == fullName);
        if (testCase is not null)
            return RunAsync(testCase, cancellationToken);

        return Task.FromResult(TestCaseReport.NotRun(fullName, DescribeMissing(testId, fullName)));
    }

    private string DescribeMissing(string testId, string fullName)
    {
        if (TestCaseExpander.TryParseRowId(testId, out var methodId, out var sourceIndex, out var rowIndex))
        {
            var rows = _cases.Values.Count(c => TestCaseExpander.TryParseRowId(c.Id, out var m, out var s, out _)
                                                && m == methodId && s == sourceIndex);
            if (rows > 0 || _cases.Values.Any(c => c.Id.StartsWith(methodId, StringComparison.Ordinal)))
            {
                return $"Test {fullName} was not run: it was row {rowIndex + 1} of its data source, "
                    + $"which now returns {rows} row(s).";
            }
        }

        return $"Test {fullName} was not found in {_assembly.GetName().Name}. It may have been renamed or removed.";
    }

    /// <summary>Runs <paramref name="testCase"/> once, collecting what the sinks gathered while it ran.</summary>
    public async Task<TestCaseReport> RunAsync(DiscoveredTestCase testCase, CancellationToken cancellationToken = default)
    {
        MotusInterop.BeginCollecting();

        var run = await TestCaseInvoker.RunAsync(
            testCase.TestClass,
            testCase.TestMethod,
            testCase.Arguments,
            DisplayNameOf(testCase),
            beforeInstance: async () =>
            {
                // Run [ClassInitialize] / [OneTimeSetUp] once per class, before its first test
                if (_initializedClasses.TryAdd(testCase.TestClass, true))
                {
                    await RunStaticLifecycleMethodsAsync(testCase.TestClass, "ClassInitializeAttribute");
                    await RunStaticLifecycleMethodsAsync(testCase.TestClass, "OneTimeSetUpAttribute");
                }
            },
            onCleanupFailure: ex => _log($"TestCleanup failed for {testCase.FullName}: {ex.Message}"),
            cancellationToken: cancellationToken);

        var (violations, metrics, coverage) = MotusInterop.EndCollecting();

        return new TestCaseReport(
            testCase.FullName,
            run.Failure is null,
            run.Duration.TotalMilliseconds,
            run.Failure?.Message,
            run.Failure?.StackTrace,
            MotusInterop.IsBrowserLost(run.Failure),
            violations,
            metrics,
            coverage);
    }

    public async Task CleanupAsync(CancellationToken cancellationToken = default)
    {
        // Run [ClassCleanup] / [OneTimeTearDown] for each class that ran a test
        foreach (var type in _initializedClasses.Keys.ToList())
        {
            if (!_initializedClasses.TryRemove(type, out _))
                continue;

            try
            {
                await RunStaticLifecycleMethodsAsync(type, "ClassCleanupAttribute");
                await RunStaticLifecycleMethodsAsync(type, "OneTimeTearDownAttribute");
            }
            catch (Exception ex)
            {
                _log($"ClassCleanup failed for {type.FullName}: {ex.InnerException?.Message ?? ex.Message}");
            }
        }

        // Run [AssemblyCleanup], which is where suites close their browsers
        try
        {
            foreach (var type in _assembly.GetExportedTypes())
            {
                foreach (var method in StaticMethodsWith(type, "AssemblyCleanupAttribute", BindingFlags.Public | BindingFlags.Static))
                    await TestCaseInvoker.InvokeStaticAsync(method, type);
            }
        }
        catch (Exception ex)
        {
            _log($"AssemblyCleanup failed for {_assembly.GetName().Name}: {ex.InnerException?.Message ?? ex.Message}");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>The test case's name without its class, such as <c>SignsIn ("owner")</c>.</summary>
    private static string DisplayNameOf(DiscoveredTestCase testCase)
    {
        var prefix = testCase.TestClass.FullName + ".";
        return testCase.FullName.StartsWith(prefix, StringComparison.Ordinal) ? testCase.FullName[prefix.Length..] : testCase.TestMethod.Name;
    }

    private static async Task RunStaticLifecycleMethodsAsync(Type type, string attributeName)
    {
        foreach (var method in StaticMethodsWith(type, attributeName, BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            await TestCaseInvoker.InvokeStaticAsync(method, type);
    }

    private static IEnumerable<MethodInfo> StaticMethodsWith(Type type, string attributeName, BindingFlags flags)
        => type.GetMethods(flags).Where(m => m.GetCustomAttributes(true).Any(a => a.GetType().Name == attributeName));
}
