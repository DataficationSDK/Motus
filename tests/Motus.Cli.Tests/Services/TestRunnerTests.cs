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

    private sealed class NullReporter : IReporter
    {
        public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
        public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
        public Task OnTestEndAsync(TestInfo test, Motus.Abstractions.TestResult result) => Task.CompletedTask;
        public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;
    }
}
