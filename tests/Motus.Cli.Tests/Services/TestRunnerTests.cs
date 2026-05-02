using System.Reflection;
using Motus.Abstractions;
using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class TestRunnerTests
{
    #region Test Fixtures (not [TestClass] — only used via reflection by TestRunner)

    public class LifecycleTrackingFixture
    {
        public static List<string> CallLog { get; } = [];

        // Attributes referenced by name in TestRunner lifecycle scanning
        [Microsoft.VisualStudio.TestTools.UnitTesting.TestInitialize]
        public void Init() => CallLog.Add("TestInitialize");

        [Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanup]
        public void Cleanup() => CallLog.Add("TestCleanup");

        public void PassingTest() => CallLog.Add("Test");

        public void FailingTest()
        {
            CallLog.Add("FailingTest");
            throw new InvalidOperationException("intentional failure");
        }
    }

    public class DisposableFixture : IDisposable
    {
        public static bool WasDisposed { get; set; }

        public void SimpleTest() { }

        public void Dispose() => WasDisposed = true;
    }

    public class AsyncDisposableFixture : IAsyncDisposable
    {
        public static bool WasDisposed { get; set; }

        public void SimpleTest() { }

        public ValueTask DisposeAsync()
        {
            WasDisposed = true;
            return ValueTask.CompletedTask;
        }
    }

    public class FlakyDisconnectFixture
    {
        public static int AttemptCount { get; set; }

        // Throws CdpDisconnectedException only on the first attempt; second attempt passes.
        public void FlakesOnce()
        {
            AttemptCount++;
            if (AttemptCount == 1)
                throw new global::Motus.CdpDisconnectedException();
        }
    }

    public class AlwaysDisconnectsFixture
    {
        public static int AttemptCount { get; set; }

        public void AlwaysFails()
        {
            AttemptCount++;
            throw new global::Motus.CdpDisconnectedException();
        }
    }

    public class TargetClosedFixture
    {
        public static int AttemptCount { get; set; }

        public void FlakesOnce()
        {
            AttemptCount++;
            if (AttemptCount == 1)
                throw new MotusTargetClosedException("page", "target-1", "target closed mid-command");
        }
    }

    public class WrappedDisconnectFixture
    {
        public static int AttemptCount { get; set; }

        // Disconnect exception buried inside an outer exception's chain — retry
        // logic must walk InnerException, not just inspect the top-level type.
        public void FlakesOnce()
        {
            AttemptCount++;
            if (AttemptCount == 1)
                throw new InvalidOperationException(
                    "outer wrapper",
                    new global::Motus.CdpDisconnectedException());
        }
    }

    public class NonRetryableFailureFixture
    {
        public static int AttemptCount { get; set; }

        public void Throws()
        {
            AttemptCount++;
            throw new InvalidOperationException("not transient");
        }
    }

    #endregion

    private static NullReporter CreateReporter() => new();

    private static DiscoveredTest MakeTest(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return new DiscoveredTest(type, method, $"{type.FullName}.{methodName}", false);
    }

    private static DiscoveredTest MakeSkippedTest(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return new DiscoveredTest(type, method, $"{type.FullName}.{methodName}", true);
    }

    [TestMethod]
    public async Task TestInitialize_RunsBeforeEachTest()
    {
        LifecycleTrackingFixture.CallLog.Clear();
        var tests = new List<DiscoveredTest> { MakeTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.PassingTest)) };

        var runner = new TestRunner(1);
        await runner.RunAsync(tests, CreateReporter());

        var initIdx = LifecycleTrackingFixture.CallLog.IndexOf("TestInitialize");
        var testIdx = LifecycleTrackingFixture.CallLog.IndexOf("Test");
        Assert.IsTrue(initIdx >= 0 && initIdx < testIdx,
            $"TestInitialize should run before test. Log: {string.Join(", ", LifecycleTrackingFixture.CallLog)}");
    }

    [TestMethod]
    public async Task TestCleanup_RunsAfterEachTest()
    {
        LifecycleTrackingFixture.CallLog.Clear();
        var tests = new List<DiscoveredTest> { MakeTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.PassingTest)) };

        var runner = new TestRunner(1);
        await runner.RunAsync(tests, CreateReporter());

        var testIdx = LifecycleTrackingFixture.CallLog.IndexOf("Test");
        var cleanupIdx = LifecycleTrackingFixture.CallLog.IndexOf("TestCleanup");
        Assert.IsTrue(testIdx >= 0 && testIdx < cleanupIdx,
            $"TestCleanup should run after test. Log: {string.Join(", ", LifecycleTrackingFixture.CallLog)}");
    }

    [TestMethod]
    public async Task TestCleanup_RunsEvenOnFailure()
    {
        LifecycleTrackingFixture.CallLog.Clear();
        var tests = new List<DiscoveredTest> { MakeTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.FailingTest)) };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter());

        Assert.AreEqual(1, result.Failed);
        CollectionAssert.Contains(LifecycleTrackingFixture.CallLog, "TestCleanup",
            "TestCleanup should run even when the test fails");
    }

    [TestMethod]
    public async Task Dispose_CalledAfterTest()
    {
        DisposableFixture.WasDisposed = false;
        var tests = new List<DiscoveredTest> { MakeTest(typeof(DisposableFixture), nameof(DisposableFixture.SimpleTest)) };

        var runner = new TestRunner(1);
        await runner.RunAsync(tests, CreateReporter());

        Assert.IsTrue(DisposableFixture.WasDisposed, "IDisposable.Dispose should be called after test");
    }

    [TestMethod]
    public async Task AsyncDispose_CalledAfterTest()
    {
        AsyncDisposableFixture.WasDisposed = false;
        var tests = new List<DiscoveredTest> { MakeTest(typeof(AsyncDisposableFixture), nameof(AsyncDisposableFixture.SimpleTest)) };

        var runner = new TestRunner(1);
        await runner.RunAsync(tests, CreateReporter());

        Assert.IsTrue(AsyncDisposableFixture.WasDisposed, "IAsyncDisposable.DisposeAsync should be called after test");
    }

    [TestMethod]
    public async Task IgnoredTest_IsSkipped()
    {
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.PassingTest)),
            MakeSkippedTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.FailingTest)),
        };
        LifecycleTrackingFixture.CallLog.Clear();

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter());

        Assert.AreEqual(1, result.Skipped, "Ignored test should be counted as skipped");
        Assert.AreEqual(1, result.Passed, "Non-ignored test should pass");
        Assert.IsFalse(LifecycleTrackingFixture.CallLog.Contains("FailingTest"),
            "Skipped test method body should not execute");
    }

    [TestMethod]
    public async Task SkippedCount_ReflectedInSummary()
    {
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.PassingTest)),
            MakeSkippedTest(typeof(LifecycleTrackingFixture), nameof(LifecycleTrackingFixture.FailingTest)),
        };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter());

        Assert.AreEqual(2, result.Total);
        Assert.AreEqual(1, result.Skipped);
        Assert.AreEqual(1, result.Passed);
        Assert.AreEqual(0, result.Failed);
    }

    [TestMethod]
    public async Task EnforceMode_FailsPassingTestWithErrorViolations()
    {
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(ViolationProducingFixture), nameof(ViolationProducingFixture.TestThatProducesViolations)),
        };

        var reporter = new A11yRecordingReporter();
        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, reporter, a11yMode: "enforce");

        Assert.AreEqual(1, result.Failed,
            "Test should fail in enforce mode when Error-severity violations are present.");
        Assert.AreEqual(0, result.Passed);
        Assert.IsTrue(reporter.ViolationCalls.Count > 0,
            "Violations should be dispatched to IAccessibilityReporter.");
    }

    [TestMethod]
    public async Task WarnMode_DoesNotFailPassingTestWithViolations()
    {
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(ViolationProducingFixture), nameof(ViolationProducingFixture.TestThatProducesViolations)),
        };

        var reporter = new A11yRecordingReporter();
        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, reporter, a11yMode: "warn");

        Assert.AreEqual(0, result.Failed,
            "Test should not fail in warn mode even with Error-severity violations.");
        Assert.AreEqual(1, result.Passed);
        Assert.IsTrue(reporter.ViolationCalls.Count > 0,
            "Violations should still be dispatched in warn mode.");
    }

    [TestMethod]
    public async Task NoA11yMode_DoesNotFailPassingTestWithViolations()
    {
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(ViolationProducingFixture), nameof(ViolationProducingFixture.TestThatProducesViolations)),
        };

        var reporter = new A11yRecordingReporter();
        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, reporter, a11yMode: null);

        Assert.AreEqual(1, result.Passed,
            "Without a11y mode, violations should not affect test results.");
    }

    public class ViolationProducingFixture
    {
        public void TestThatProducesViolations()
        {
            // Simulate what the AccessibilityAuditHook does during test execution
            AccessibilityViolationSink.Add(new AccessibilityViolation(
                "a11y-alt-text", AccessibilityViolationSeverity.Error,
                "Image missing alt text", null, null, null, "img.hero"));
        }
    }

    [TestMethod]
    public async Task Retries_OnCdpDisconnect_PassesOnSecondAttempt()
    {
        FlakyDisconnectFixture.AttemptCount = 0;
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(FlakyDisconnectFixture), nameof(FlakyDisconnectFixture.FlakesOnce)),
        };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter(),
            a11yMode: null, enforcePerfBudget: false, coverageReporters: null, maxRetries: 2);

        Assert.AreEqual(1, result.Passed, "Test should be reported as passing after retry succeeded.");
        Assert.AreEqual(0, result.Failed);
        Assert.AreEqual(2, FlakyDisconnectFixture.AttemptCount,
            "Test should have been invoked exactly twice: 1 initial attempt + 1 retry.");
    }

    [TestMethod]
    public async Task Retries_OnMotusTargetClosedException_PassesOnSecondAttempt()
    {
        TargetClosedFixture.AttemptCount = 0;
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(TargetClosedFixture), nameof(TargetClosedFixture.FlakesOnce)),
        };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter(),
            a11yMode: null, enforcePerfBudget: false, coverageReporters: null, maxRetries: 2);

        Assert.AreEqual(1, result.Passed);
        Assert.AreEqual(2, TargetClosedFixture.AttemptCount);
    }

    [TestMethod]
    public async Task Retries_WhenDisconnectIsWrappedInOuterException()
    {
        WrappedDisconnectFixture.AttemptCount = 0;
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(WrappedDisconnectFixture), nameof(WrappedDisconnectFixture.FlakesOnce)),
        };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter(),
            a11yMode: null, enforcePerfBudget: false, coverageReporters: null, maxRetries: 2);

        Assert.AreEqual(1, result.Passed,
            "Retry must inspect InnerException, not just the top-level exception type.");
        Assert.AreEqual(2, WrappedDisconnectFixture.AttemptCount);
    }

    [TestMethod]
    public async Task DoesNotRetry_WhenFailureIsNotTransient()
    {
        NonRetryableFailureFixture.AttemptCount = 0;
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(NonRetryableFailureFixture), nameof(NonRetryableFailureFixture.Throws)),
        };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter(),
            a11yMode: null, enforcePerfBudget: false, coverageReporters: null, maxRetries: 5);

        Assert.AreEqual(0, result.Passed);
        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(1, NonRetryableFailureFixture.AttemptCount,
            "Non-transient failures must not be retried — would mask real test bugs.");
    }

    [TestMethod]
    public async Task ExhaustsRetries_OnPersistentDisconnect()
    {
        AlwaysDisconnectsFixture.AttemptCount = 0;
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(AlwaysDisconnectsFixture), nameof(AlwaysDisconnectsFixture.AlwaysFails)),
        };

        var runner = new TestRunner(1);
        var result = await runner.RunAsync(tests, CreateReporter(),
            a11yMode: null, enforcePerfBudget: false, coverageReporters: null, maxRetries: 2);

        Assert.AreEqual(0, result.Passed);
        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(3, AlwaysDisconnectsFixture.AttemptCount,
            "On a persistent disconnect, runner must invoke the test exactly maxRetries+1 times.");
    }

    [TestMethod]
    public async Task DefaultRetries_IsZero()
    {
        AlwaysDisconnectsFixture.AttemptCount = 0;
        var tests = new List<DiscoveredTest>
        {
            MakeTest(typeof(AlwaysDisconnectsFixture), nameof(AlwaysDisconnectsFixture.AlwaysFails)),
        };

        var runner = new TestRunner(1);
        // Don't pass maxRetries — verify the default is 0 (no retries).
        var result = await runner.RunAsync(tests, CreateReporter(),
            a11yMode: null, enforcePerfBudget: false, coverageReporters: null);

        Assert.AreEqual(1, result.Failed);
        Assert.AreEqual(1, AlwaysDisconnectsFixture.AttemptCount,
            "Default behaviour with no --retries flag must be a single attempt.");
    }

    private sealed class NullReporter : IReporter
    {
        public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
        public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
        public Task OnTestEndAsync(TestInfo test, Motus.Abstractions.TestResult result) => Task.CompletedTask;
        public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;
    }

    private sealed class A11yRecordingReporter : IReporter, IAccessibilityReporter
    {
        internal List<(string RuleId, string TestName)> ViolationCalls { get; } = [];

        public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
        public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
        public Task OnTestEndAsync(TestInfo test, Motus.Abstractions.TestResult result) => Task.CompletedTask;
        public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;

        public Task OnAccessibilityViolationAsync(AccessibilityViolation violation, TestInfo test)
        {
            ViolationCalls.Add((violation.RuleId, test.TestName));
            return Task.CompletedTask;
        }
    }
}
