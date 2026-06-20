using System.Text.Json;
using Motus.Abstractions;
using Motus.Cli.Services;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class FlakeHistoryReporterTests
{
    private string _outputPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _outputPath = Path.Combine(Path.GetTempPath(), $"motus-flake-{Guid.NewGuid()}.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_outputPath))
            File.Delete(_outputPath);
    }

    private static Dictionary<string, FlakeRecord> ReadHistory(string path)
    {
        // The reporter writes camelCase keys via source generation; read them back
        // case-insensitively so the reflection deserializer maps onto the record.
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Dictionary<string, FlakeRecord>>(
            json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    [TestMethod]
    public async Task RecordsRunFailureAndFlakyCounts()
    {
        var reporter = new FlakeHistoryReporter(_outputPath);

        await reporter.OnTestEndAsync(new TestInfo("Ns.Pass", "Suite"),
            new TestResult("Ns.Pass", true, 10));
        await reporter.OnTestEndAsync(new TestInfo("Ns.Fail", "Suite"),
            new TestResult("Ns.Fail", false, 10, "boom"));
        await reporter.OnTestEndAsync(new TestInfo("Ns.Flaky", "Suite"),
            new TestResult("Ns.Flaky", true, 10, Flaky: true, Attempts: 2));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite", 2, 1, 0, 30, Flaky: 1));

        var history = ReadHistory(_outputPath);

        Assert.AreEqual(1, history["Ns.Pass"].Runs);
        Assert.AreEqual(0, history["Ns.Pass"].Failures);
        Assert.AreEqual(0, history["Ns.Pass"].FlakyPasses);

        Assert.AreEqual(1, history["Ns.Fail"].Runs);
        Assert.AreEqual(1, history["Ns.Fail"].Failures);

        Assert.AreEqual(1, history["Ns.Flaky"].Runs);
        Assert.AreEqual(0, history["Ns.Flaky"].Failures);
        Assert.AreEqual(1, history["Ns.Flaky"].FlakyPasses);
    }

    [TestMethod]
    public async Task AccumulatesAcrossRuns()
    {
        // Run 1: the test fails.
        var run1 = new FlakeHistoryReporter(_outputPath);
        await run1.OnTestEndAsync(new TestInfo("Ns.Test", "Suite"),
            new TestResult("Ns.Test", false, 10, "boom"));
        await run1.OnTestRunEndAsync(new TestRunSummary("Suite", 0, 1, 0, 10));

        // Run 2: the same test is flaky (passes after a retry).
        var run2 = new FlakeHistoryReporter(_outputPath);
        await run2.OnTestEndAsync(new TestInfo("Ns.Test", "Suite"),
            new TestResult("Ns.Test", true, 10, Flaky: true, Attempts: 2));
        await run2.OnTestRunEndAsync(new TestRunSummary("Suite", 1, 0, 0, 10, Flaky: 1));

        var history = ReadHistory(_outputPath);

        Assert.AreEqual(2, history["Ns.Test"].Runs, "Run counts accumulate across runs.");
        Assert.AreEqual(1, history["Ns.Test"].Failures);
        Assert.AreEqual(1, history["Ns.Test"].FlakyPasses);
    }

    [TestMethod]
    public async Task CorruptHistoryFileDoesNotThrow()
    {
        await File.WriteAllTextAsync(_outputPath, "{ this is not valid json");

        var reporter = new FlakeHistoryReporter(_outputPath);
        await reporter.OnTestEndAsync(new TestInfo("Ns.Test", "Suite"),
            new TestResult("Ns.Test", true, 10));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite", 1, 0, 0, 10));

        // The corrupt file is replaced with a valid, fresh history.
        var history = ReadHistory(_outputPath);
        Assert.AreEqual(1, history["Ns.Test"].Runs);
    }
}
