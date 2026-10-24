using System.Reflection;
using System.Runtime.CompilerServices;
using Motus.Abstractions;
using Motus.Cli.Services;
using Motus.Cli.Services.Reporters;
using Motus.Testing.MSTest;

namespace Motus.Cli.Tests.Services;

/// <summary>
/// Runs <see cref="MotusTestBase"/> tests through <c>motus run</c> with failure tracing on, and
/// checks that only the failing test leaves a trace behind.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class FailureTraceIntegrationTests
{
    // Not a [TestClass]: run only through TestRunner.
    public class TracedFixture : MotusTestBase
    {
        public async Task Passes() => await Page.GotoAsync("data:text/html,<p>passes</p>");

        public async Task Fails()
        {
            await Page.GotoAsync("data:text/html,<p>fails</p>");
            throw new InvalidOperationException("intentional failure");
        }
    }

    private static bool s_browserAvailable;

    [ClassInitialize]
    public static async Task ClassSetup(TestContext _)
    {
        try
        {
            var probe = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
            await probe.CloseAsync();
            s_browserAvailable = true;
        }
        catch (FileNotFoundException)
        {
            s_browserAvailable = false;
        }
    }

    [ClassCleanup]
    public static void ClassTeardown()
    {
        try
        {
            if (MotusConfigLoader.Config.Failure?.TracePath is { } directory)
                Directory.Delete(directory, recursive: true);
        }
        catch { }
    }

    [TestInitialize]
    public void Setup()
    {
        if (!s_browserAvailable)
            Assert.Inconclusive("No browser found; skipping integration tests.");

        Assert.IsTrue(MotusConfigLoader.Config.Failure?.Trace is true, "failure tracing should be on for this assembly");
    }

    [TestMethod]
    public async Task PassingTest_SavesNoTrace_FailingTest_SavesOne()
    {
        var traceDirectory = MotusConfigLoader.Config.Failure!.TracePath!;

        var result = await new TestRunner(1).RunAsync(
            [MakeTest(nameof(TracedFixture.Passes)), MakeTest(nameof(TracedFixture.Fails))],
            new ConsoleReporter());

        Assert.AreEqual(1, result.Passed);
        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(0, TracesFor(traceDirectory, nameof(TracedFixture.Passes)).Length,
            "a passing test should leave no failure trace");
        Assert.AreEqual(1, TracesFor(traceDirectory, nameof(TracedFixture.Fails)).Length,
            "a failing test should still leave its failure trace");
    }

    private static DiscoveredTest MakeTest(string methodName)
    {
        var type = typeof(TracedFixture);
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return new DiscoveredTest(type, method, $"{type.FullName}.{methodName}", false);
    }

    private static string[] TracesFor(string directory, string methodName)
        => Directory.Exists(directory)
            ? Directory.GetFiles(directory, $"trace-{nameof(TracedFixture)}.{methodName}-*.zip")
            : [];
}

internal static class FailureTracingSetup
{
    /// <summary>
    /// Failure tracing is read from configuration once per process, so it is switched on as this
    /// assembly loads, before anything reads it, with traces going to a folder of this run's own.
    /// Only <see cref="MotusTestBase"/> acts on it, and only <see cref="FailureTraceIntegrationTests"/>
    /// uses that here.
    /// </summary>
#pragma warning disable CA2255 // a test assembly, not a library
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void EnableFailureTracing()
    {
        Environment.SetEnvironmentVariable("MOTUS_FAILURES_TRACE", "true");
        Environment.SetEnvironmentVariable(
            "MOTUS_FAILURES_TRACE_PATH",
            Path.Combine(Path.GetTempPath(), $"motus-cli-tests-traces-{Guid.NewGuid():N}"));
    }
}
