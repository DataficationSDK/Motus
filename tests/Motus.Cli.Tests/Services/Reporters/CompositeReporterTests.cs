using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class CompositeReporterTests
{
    private sealed class RecordingReporter : IReporter, IAccessibilityReporter
    {
        internal List<string> Calls { get; } = [];

        public Task OnTestRunStartAsync(TestSuiteInfo suite) { Calls.Add("RunStart"); return Task.CompletedTask; }
        public Task OnTestStartAsync(TestInfo test) { Calls.Add("TestStart"); return Task.CompletedTask; }
        public Task OnTestEndAsync(TestInfo test, TestResult result) { Calls.Add("TestEnd"); return Task.CompletedTask; }
        public Task OnTestRunEndAsync(TestRunSummary summary) { Calls.Add("RunEnd"); return Task.CompletedTask; }
        public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
        {
            Calls.Add($"A11Y:{violation.RuleId}");
            return Task.CompletedTask;
        }
    }

    private sealed class PlainReporter : IReporter
    {
        internal List<string> Calls { get; } = [];

        public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
        public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
        public Task OnTestEndAsync(TestInfo test, TestResult result) => Task.CompletedTask;
        public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;
    }

    private sealed class ThrowingReporter : IReporter
    {
        public Task OnTestRunStartAsync(TestSuiteInfo suite) => throw new InvalidOperationException("boom");
        public Task OnTestStartAsync(TestInfo test) => throw new InvalidOperationException("boom");
        public Task OnTestEndAsync(TestInfo test, TestResult result) => throw new InvalidOperationException("boom");
        public Task OnTestRunEndAsync(TestRunSummary summary) => throw new InvalidOperationException("boom");
    }

    [TestMethod]
    public async Task FanOut_BothReportersReceiveCalls()
    {
        var r1 = new RecordingReporter();
        var r2 = new RecordingReporter();
        var composite = new CompositeReporter([r1, r2]);

        var suite = new TestSuiteInfo("Suite1", 1);
        var test = new TestInfo("Test1", "Suite1");
        var result = new TestResult("Test1", true, 100);
        var summary = new TestRunSummary("Suite1", 1, 0, 0, 100);

        await composite.OnTestRunStartAsync(suite);
        await composite.OnTestStartAsync(test);
        await composite.OnTestEndAsync(test, result);
        await composite.OnTestRunEndAsync(summary);

        Assert.AreEqual(4, r1.Calls.Count);
        Assert.AreEqual(4, r2.Calls.Count);
    }

    [TestMethod]
    public async Task ThrowingReporter_DoesNotBlockSecond()
    {
        var recording = new RecordingReporter();
        var composite = new CompositeReporter([new ThrowingReporter(), recording]);

        await composite.OnTestRunStartAsync(new TestSuiteInfo("Suite1", 1));
        await composite.OnTestStartAsync(new TestInfo("Test1", "Suite1"));
        await composite.OnTestEndAsync(new TestInfo("Test1", "Suite1"), new TestResult("Test1", true, 50));
        await composite.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 50));

        Assert.AreEqual(4, recording.Calls.Count);
    }

    [TestMethod]
    public async Task AccessibilityViolation_DispatchedToA11yReportersOnly()
    {
        var a11yReporter = new RecordingReporter();
        var plainReporter = new PlainReporter();
        var composite = new CompositeReporter([a11yReporter, plainReporter]);

        var violation = new AccessibilityViolation(
            "a11y-alt-text", AccessibilityViolationSeverity.Error,
            "Missing alt", null, null, null, null);
        var test = new TestInfo("Test1", "Suite1");

        await composite.OnAccessibilityViolationAsync(violation, test);

        Assert.AreEqual(1, a11yReporter.Calls.Count);
        Assert.AreEqual("A11Y:a11y-alt-text", a11yReporter.Calls[0]);
        Assert.AreEqual(0, plainReporter.Calls.Count);
    }
}
