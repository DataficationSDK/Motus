using System.CommandLine;
using System.Reflection;
using Motus.Abstractions;
using Motus.Cli.Commands;
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
            CollectionAssert.AreEqual(new[] { path }, discovery.FailedAssemblies.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// An assembly whose test host cannot list its tests fails the run. Before, it was reported on
    /// standard error and the run exited 0 when nothing else failed, so it looked green.
    /// </summary>
    [TestMethod]
    public async Task AssemblyThatCannotBeLoaded_FailsTheRun()
    {
        var path = Path.Combine(Path.GetTempPath(), $"motus-not-an-assembly-{Guid.NewGuid():N}.dll");
        await File.WriteAllTextAsync(path, "not an assembly");
        try
        {
            var exitCode = await RunCommand.Build().Parse([path, "--reporter", "console"]).InvokeAsync();

            Assert.AreEqual(1, exitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// The test host runs a suite on the Motus its project references, and the oldest published
    /// package lacks most of what the runner reaches into Motus for. Its tests still run and report
    /// their own results; only the extras go. Before, a suite on 1.0.10 crashed the test host and
    /// one on 1.0.11 to 1.0.14 failed every test with a TypeLoadException.
    /// </summary>
    [TestMethod]
    public async Task SuiteOnTheOldestPublishedMotus_RunsWithItsOwnResults()
    {
        var reporter = new RecordingReporter();
        Environment.SetEnvironmentVariable("MOTUS_OLD_MOTUS_FIXTURE_FAIL", "1");
        try
        {
            await using var discovery = new TestDiscovery();
            var tests = await discovery.DiscoverAsync([FixturePath("OldMotusFixtureOutput", "Motus.Fixtures.OldMotus.Tests.dll", CurrentFramework)], null);
            var result = await new TestRunner(1).RunAsync(tests, reporter);

            Assert.AreEqual(6, tests.Count, string.Join(" | ", tests.Select(t => t.FullName)));
            Assert.AreEqual(5, result.Passed, reporter.Failures);
            Assert.AreEqual(1, result.Failed, reporter.Failures);
            StringAssert.Contains(reporter.ErrorOf("FailsWhenAsked"), "failed as asked");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MOTUS_OLD_MOTUS_FIXTURE_FAIL", null);
        }
    }

    /// <summary>Overloads share a name but not an identity, so each runs, rather than one of them twice.</summary>
    [TestMethod]
    public async Task OverloadedTests_EachRun()
    {
        var reporter = new RecordingReporter();
        await using var discovery = new TestDiscovery();
        var tests = await discovery.DiscoverAsync([typeof(OverloadFixture).Assembly.Location], nameof(OverloadFixture));

        var result = await new TestRunner(1).RunAsync(tests, reporter);

        Assert.AreEqual(2, tests.Select(t => t.TestId).Distinct().Count(), string.Join(" | ", tests.Select(t => t.TestId)));
        Assert.AreEqual(1, result.Passed, "the overload without parameters passes");
        Assert.AreEqual(1, result.Failed, "the overload with one fails");
    }

    /// <summary>
    /// Each shard is a separate motus run with test hosts of its own, where a data source can name its
    /// rows differently. Shards are assigned by the rows' identity, so every process agrees on them.
    /// </summary>
    [TestMethod]
    public async Task Shards_AreTheSameInEveryProcess_WhenRowNamesChange()
    {
        await using var first = new TestDiscovery();
        await using var second = new TestDiscovery();
        var path = typeof(ChangingRowNamesFixture).Assembly.Location;

        var once = await first.DiscoverAsync([path], nameof(ChangingRowNamesFixture));
        var again = await second.DiscoverAsync([path], nameof(ChangingRowNamesFixture));

        Assert.AreEqual(ChangingRowNamesFixture.RowCount, once.Count);
        Assert.IsFalse(once.Select(t => t.FullName).Intersect(again.Select(t => t.FullName)).Any(), "the rows' names should differ between processes");
        CollectionAssert.AreEqual(
            ShardSelector.Select(once, 1, 2).Select(t => t.TestId).ToList(),
            ShardSelector.Select(again, 1, 2).Select(t => t.TestId).ToList());
    }

    private static string CurrentFramework => $"net{Environment.Version.Major}.0";

    private static string WebAppFixturePath(string framework)
        => FixturePath("WebAppFixtureOutput", "Motus.Fixtures.WebApp.Tests.dll", framework);

    private static string FixturePath(string key, string fileName, string framework)
    {
        var output = typeof(TestHostIsolationTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(a => a.Key == key).Value!;
        return Path.GetFullPath(Path.Combine(output, framework, fileName));
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

/// <summary>
/// A data source that names its rows differently each time it is read, as one that puts a Guid or a
/// time in a row does.
/// </summary>
[TestClass]
public class ChangingRowNamesFixture
{
    public const int RowCount = 8;

    public static IEnumerable<object[]> Rows => Enumerable.Range(0, RowCount).Select(i => new object[] { i, Guid.NewGuid().ToString("N") });

    [TestMethod]
    [DynamicData(nameof(Rows))]
    public void RunsEachRow(int index, string token) => Assert.IsTrue(index < RowCount && token.Length == 32);
}

/// <summary>Marks <see cref="OverloadFixture"/> a test class for the runners, which match by name, but not for MSTest.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TestFixtureAttribute : Attribute;

/// <summary>Marks a test method of <see cref="OverloadFixture"/> for the runners, but not for MSTest.</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class TestAttribute : Attribute;

/// <summary>Two test methods with one name, as NUnit allows.</summary>
[TestFixture]
public class OverloadFixture
{
    [Test]
    public void Overloaded()
    {
    }

    [Test]
    public void Overloaded(int value = 1) => throw new InvalidOperationException($"the overload with a parameter ran ({value})");
}
