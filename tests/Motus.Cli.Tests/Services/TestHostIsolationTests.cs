using System.Reflection;
using Motus.Abstractions;
using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

/// <summary>
/// <c>motus run</c> runs each test assembly in a test host process of its own, started from the
/// assembly's output folder with its own dependencies, so that a suite runs as it does under
/// <c>dotnet test</c> rather than inside the runner.
/// </summary>
[TestClass]
public class TestHostIsolationTests
{
    private const string CrashVariable = "MOTUS_TEST_HOST_CRASH_FIXTURE";

    /// <summary>
    /// The fixture suite starts an ASP.NET Core app with <c>WebApplicationFactory</c>, which needs the
    /// suite's output folder as its base and current directory, and depends on SkiaSharp 3 where the
    /// runner depends on SkiaSharp 2. Loaded into the runner, every one of its tests failed.
    /// </summary>
    [TestMethod]
    public async Task WebAppSuite_RunsWithItsOwnDependenciesAndFolder()
    {
        var reporter = new RecordingReporter();
        await using var discovery = new TestDiscovery();

        var tests = await discovery.DiscoverAsync([WebAppFixturePath(CurrentFramework)], null);
        var result = await new TestRunner(2).RunAsync(tests, reporter);

        Assert.AreEqual(4, tests.Count, string.Join(" | ", tests.Select(t => t.FullName)));
        Assert.AreEqual(4, result.Passed, reporter.Failures);
        Assert.AreEqual("Motus.Fixtures.WebApp.Tests", reporter.SuiteName);
    }

    /// <summary>
    /// The runner is built for the newest .NET it supports and cannot load on an older one, so a
    /// suite built for an older one runs on the runner's.
    /// </summary>
    [TestMethod]
    public async Task SuiteBuiltForAnOlderDotNet_RunsOnTheRunnersDotNet()
    {
        if (Environment.Version.Major <= 8)
            Assert.Inconclusive("Runs only where the runner is built for a newer .NET than the fixture's oldest.");

        var path = WebAppFixturePath("net8.0");
        if (!File.Exists(path))
            Assert.Inconclusive($"The net8.0 fixture has not been built: {path}");

        var reporter = new RecordingReporter();
        await using var discovery = new TestDiscovery();

        var tests = await discovery.DiscoverAsync([path], null);
        var result = await new TestRunner(1).RunAsync(tests, reporter);

        Assert.AreEqual(4, result.Passed, reporter.Failures);
    }

    /// <summary>
    /// A test that ends the process takes its test host with it, not the runner: the test it was
    /// running fails saying so, and so does each test of that assembly after it.
    /// </summary>
    [TestMethod]
    public async Task TestHostThatExits_FailsTheRunningTestAndTheRest()
    {
        var reporter = new RecordingReporter();
        Environment.SetEnvironmentVariable(CrashVariable, "1");
        try
        {
            await using var discovery = new TestDiscovery();
            var tests = await discovery.DiscoverAsync([typeof(TestHostCrashFixture).Assembly.Location], nameof(TestHostCrashFixture));
            tests = tests.OrderBy(t => t.MethodName == nameof(TestHostCrashFixture.ExitsTheProcess) ? 0 : 1).ToList();

            var result = await new TestRunner(1).RunAsync(tests, reporter);

            Assert.AreEqual(2, result.Failed, reporter.Failures);
            StringAssert.Contains(reporter.ErrorOf(nameof(TestHostCrashFixture.ExitsTheProcess)), "exited unexpectedly with code 3");
            StringAssert.Contains(reporter.ErrorOf(nameof(TestHostCrashFixture.RunsAfterTheExit)), "exited unexpectedly with code 3");
            StringAssert.Contains(reporter.ErrorOf(nameof(TestHostCrashFixture.RunsAfterTheExit)), "did not run");
        }
        finally
        {
            Environment.SetEnvironmentVariable(CrashVariable, null);
        }
    }

    [TestMethod]
    public async Task FileThatIsNotAnAssembly_IsSkipped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"motus-not-an-assembly-{Guid.NewGuid():N}.dll");
        await File.WriteAllTextAsync(path, "not an assembly");
        try
        {
            await using var discovery = new TestDiscovery();
            var tests = await discovery.DiscoverAsync([path], null);

            Assert.AreEqual(0, tests.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CurrentFramework => $"net{Environment.Version.Major}.0";

    private static string WebAppFixturePath(string framework)
    {
        var output = typeof(TestHostIsolationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == "WebAppFixtureOutput").Value!;
        return Path.GetFullPath(Path.Combine(output, framework, "Motus.Fixtures.WebApp.Tests.dll"));
    }

    private sealed class RecordingReporter : IReporter
    {
        private readonly Dictionary<string, string?> _errors = new(StringComparer.Ordinal);

        public string? SuiteName { get; private set; }

        public string Failures
        {
            get
            {
                lock (_errors)
                    return string.Join(" | ", _errors.Where(e => e.Value is not null).Select(e => $"{e.Key}: {e.Value}"));
            }
        }

        public string ErrorOf(string methodName)
        {
            lock (_errors)
                return _errors.Single(e => e.Key.EndsWith("." + methodName, StringComparison.Ordinal)).Value ?? "";
        }

        public Task OnTestRunStartAsync(TestSuiteInfo suite)
        {
            SuiteName = suite.SuiteName;
            return Task.CompletedTask;
        }

        public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;

        public Task OnTestEndAsync(TestInfo test, Motus.Abstractions.TestResult result)
        {
            lock (_errors)
                _errors[test.TestName] = result.Passed ? null : result.ErrorMessage;
            return Task.CompletedTask;
        }

        public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;
    }
}

/// <summary>
/// Ends its process when <c>MOTUS_TEST_HOST_CRASH_FIXTURE</c> is set, as the test above sets it for
/// the test host it starts. Under <c>dotnet test</c> both tests pass.
/// </summary>
[TestClass]
public class TestHostCrashFixture
{
    [TestMethod]
    public void ExitsTheProcess()
    {
        if (Environment.GetEnvironmentVariable("MOTUS_TEST_HOST_CRASH_FIXTURE") == "1")
            Environment.Exit(3);
    }

    [TestMethod]
    public void RunsAfterTheExit()
    {
    }
}
