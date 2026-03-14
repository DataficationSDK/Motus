using Motus.Abstractions;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Tests.Plugins;

internal sealed class RecordingReporter : IReporter
{
    internal List<string> Calls { get; } = [];

    public Task OnTestRunStartAsync(TestSuiteInfo suite)
    {
        Calls.Add($"RunStart:{suite.SuiteName}:{suite.TestCount}");
        return Task.CompletedTask;
    }

    public Task OnTestStartAsync(TestInfo test)
    {
        Calls.Add($"TestStart:{test.TestName}");
        return Task.CompletedTask;
    }

    public Task OnTestEndAsync(TestInfo test, TestResult result)
    {
        var status = result.Passed ? "pass" : "fail";
        Calls.Add($"TestEnd:{result.TestName}:{status}");
        return Task.CompletedTask;
    }

    public Task OnTestRunEndAsync(TestRunSummary summary)
    {
        Calls.Add($"RunEnd:{summary.Passed}p:{summary.Failed}f:{summary.Skipped}s");
        return Task.CompletedTask;
    }
}

internal sealed class ThrowingReporter : IReporter
{
    public Task OnTestRunStartAsync(TestSuiteInfo suite) => throw new InvalidOperationException("boom");
    public Task OnTestStartAsync(TestInfo test) => throw new InvalidOperationException("boom");
    public Task OnTestEndAsync(TestInfo test, TestResult result) => throw new InvalidOperationException("boom");
    public Task OnTestRunEndAsync(TestRunSummary summary) => throw new InvalidOperationException("boom");
}

[TestClass]
public class ReporterCollectionTests
{
    [TestMethod]
    public async Task FireOnTestRunStart_CallsAllReporters()
    {
        var collection = new ReporterCollection();
        var r1 = new RecordingReporter();
        var r2 = new RecordingReporter();
        collection.Add(r1);
        collection.Add(r2);

        await collection.FireOnTestRunStartAsync(new TestSuiteInfo("Suite1", 5));

        Assert.AreEqual(1, r1.Calls.Count);
        Assert.AreEqual("RunStart:Suite1:5", r1.Calls[0]);
        Assert.AreEqual(1, r2.Calls.Count);
        Assert.AreEqual("RunStart:Suite1:5", r2.Calls[0]);
    }

    [TestMethod]
    public async Task ThrowingReporter_DoesNotBlockOthers()
    {
        var collection = new ReporterCollection();
        collection.Add(new ThrowingReporter());
        var recording = new RecordingReporter();
        collection.Add(recording);

        await collection.FireOnTestRunStartAsync(new TestSuiteInfo("Suite1", 3));
        await collection.FireOnTestStartAsync(new TestInfo("Test1", "Suite1"));
        await collection.FireOnTestEndAsync(
            new TestInfo("Test1", "Suite1"),
            new TestResult("Test1", true, 100));
        await collection.FireOnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        Assert.AreEqual(4, recording.Calls.Count);
    }

    [TestMethod]
    public async Task NoReporters_FireIsNoop()
    {
        var collection = new ReporterCollection();

        await collection.FireOnTestRunStartAsync(new TestSuiteInfo("Suite1", 0));
        await collection.FireOnTestStartAsync(new TestInfo("Test1", "Suite1"));
        await collection.FireOnTestEndAsync(
            new TestInfo("Test1", "Suite1"),
            new TestResult("Test1", true, 50));
        await collection.FireOnTestRunEndAsync(new TestRunSummary("Suite1", 0, 0, 0, 0));
    }

    [TestMethod]
    public async Task FullLifecycle_CorrectOrder()
    {
        var collection = new ReporterCollection();
        var recording = new RecordingReporter();
        collection.Add(recording);

        await collection.FireOnTestRunStartAsync(new TestSuiteInfo("Suite1", 1));
        await collection.FireOnTestStartAsync(new TestInfo("Test1", "Suite1"));
        await collection.FireOnTestEndAsync(
            new TestInfo("Test1", "Suite1"),
            new TestResult("Test1", true, 42));
        await collection.FireOnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 42));

        Assert.AreEqual(4, recording.Calls.Count);
        Assert.IsTrue(recording.Calls[0].StartsWith("RunStart:"));
        Assert.IsTrue(recording.Calls[1].StartsWith("TestStart:"));
        Assert.IsTrue(recording.Calls[2].StartsWith("TestEnd:"));
        Assert.IsTrue(recording.Calls[3].StartsWith("RunEnd:"));
    }
}
