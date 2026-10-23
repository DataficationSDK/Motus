using System.Collections.Concurrent;
using System.Reflection;
using Motus.Abstractions;
using Motus.Cli.Commands;
using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

/// <summary>
/// MSTest features that <c>motus run</c> has to provide the way MSTest's own adapter does, so that a
/// suite that runs under <c>dotnet test</c> runs unchanged under the runner.
/// </summary>
[TestClass]
public class TestRunnerMSTestTests
{
    #region Fixtures (not [TestClass]: run only through TestRunner)

    public class ContextFixture
    {
        public static List<string> Log { get; } = [];

        public TestContext TestContext { get; set; } = null!;

        [TestInitialize]
        public void Init() => Log.Add($"init {TestContext?.TestName} {TestContext?.CurrentTestOutcome}");

        // The check MotusTestBase makes before it decides whether to keep a failure trace.
        [TestCleanup]
        public void Cleanup() => Log.Add($"cleanup {TestContext?.CurrentTestOutcome} saveTrace={TestContext?.CurrentTestOutcome != UnitTestOutcome.Passed}");

        public void Passes()
        {
            TestContext.WriteLine("hello from {0}", TestContext.TestName);
            Log.Add($"class {TestContext.FullyQualifiedTestClassName}");
            Log.Add($"token {TestContext.CancellationTokenSource is not null}");
        }

        public void Fails() => throw new InvalidOperationException("intentional failure");

        public void IsInconclusive() => Assert.Inconclusive("not decided");
    }

    public class ConstructorContextFixture(TestContext testContext)
    {
        public static string? SeenName { get; set; }

        public void UsesContext() => SeenName = testContext.TestName;
    }

    public class ClassInitializeFixture
    {
        public static TestContext? Received { get; set; }

        [ClassInitialize]
        public static void Setup(TestContext context) => Received = context;

        public void Test() { }
    }

    public abstract class OrderBase
    {
        public static List<string> Log { get; } = [];

        [TestInitialize]
        public void BaseInit() => Log.Add("base init");

        [TestCleanup]
        public void BaseCleanup() => Log.Add("base cleanup");
    }

    public class OrderFixture : OrderBase
    {
        [TestInitialize]
        public void DerivedInit() => Log.Add("derived init");

        [TestCleanup]
        public void DerivedCleanup() => Log.Add("derived cleanup");

        public void Test() => Log.Add("test");
    }

    public abstract class NUnitOrderBase
    {
        public static List<string> Log { get; } = [];

        [SetUp]
        public void BaseSetUp() => Log.Add("base setup");

        [TearDown]
        public void BaseTearDown() => Log.Add("base teardown");
    }

    public class NUnitOrderFixture : NUnitOrderBase
    {
        [SetUp]
        public void DerivedSetUp() => Log.Add("derived setup");

        [TearDown]
        public void DerivedTearDown() => Log.Add("derived teardown");

        public void Test() => Log.Add("test");
    }

    public abstract class ReleasingBase
    {
        public static bool Released { get; set; }

        [TestCleanup]
        public void Release() => Released = true;
    }

    public class ThrowingCleanupFixture : ReleasingBase
    {
        [TestCleanup]
        public void Throws() => throw new InvalidOperationException("derived cleanup failed");

        public void Test() { }
    }

    // The runners find NUnit's lifecycle methods by attribute name, so these stand in for NUnit's own.
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SetUpAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TearDownAttribute : Attribute;

    #endregion

    private static NullReporter Reporter { get; } = new();

    private static DiscoveredTest MakeTest(Type type, string methodName)
    {
        var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance)!;
        return new DiscoveredTest(type, method, $"{type.FullName}.{methodName}", false);
    }

    private static Task<TestRunResult> RunAsync(params DiscoveredTest[] tests)
        => new TestRunner(1).RunAsync([.. tests], Reporter);

    [TestMethod]
    public async Task TestContext_IsSetBeforeTestInitialize_AndDescribesTheTest()
    {
        ContextFixture.Log.Clear();

        var result = await RunAsync(MakeTest(typeof(ContextFixture), nameof(ContextFixture.Passes)));

        Assert.AreEqual(1, result.Passed);
        CollectionAssert.Contains(ContextFixture.Log, $"init {nameof(ContextFixture.Passes)} InProgress");
        CollectionAssert.Contains(ContextFixture.Log, $"class {typeof(ContextFixture).FullName}");
        CollectionAssert.Contains(ContextFixture.Log, "token True");
    }

    [TestMethod]
    public async Task PassingTest_CleanupSeesPassed_SoNoFailureTraceIsKept()
    {
        ContextFixture.Log.Clear();

        await RunAsync(MakeTest(typeof(ContextFixture), nameof(ContextFixture.Passes)));

        CollectionAssert.Contains(ContextFixture.Log, "cleanup Passed saveTrace=False");
    }

    [TestMethod]
    public async Task FailingTest_CleanupSeesFailed_SoTheFailureTraceIsKept()
    {
        ContextFixture.Log.Clear();

        var result = await RunAsync(MakeTest(typeof(ContextFixture), nameof(ContextFixture.Fails)));

        Assert.AreEqual(1, result.Failed);
        CollectionAssert.Contains(ContextFixture.Log, "cleanup Failed saveTrace=True");
    }

    [TestMethod]
    public async Task InconclusiveTest_CleanupSeesInconclusive()
    {
        ContextFixture.Log.Clear();

        await RunAsync(MakeTest(typeof(ContextFixture), nameof(ContextFixture.IsInconclusive)));

        CollectionAssert.Contains(ContextFixture.Log, "cleanup Inconclusive saveTrace=True");
    }

    [TestMethod]
    public async Task TestContext_CanBeTakenByTheConstructor()
    {
        ConstructorContextFixture.SeenName = null;

        var result = await RunAsync(MakeTest(typeof(ConstructorContextFixture), nameof(ConstructorContextFixture.UsesContext)));

        Assert.AreEqual(1, result.Passed);
        Assert.AreEqual(nameof(ConstructorContextFixture.UsesContext), ConstructorContextFixture.SeenName);
    }

    [TestMethod]
    public async Task ClassInitialize_ReceivesATestContextForItsClass()
    {
        ClassInitializeFixture.Received = null;

        await RunAsync(MakeTest(typeof(ClassInitializeFixture), nameof(ClassInitializeFixture.Test)));

        Assert.IsNotNull(ClassInitializeFixture.Received);
        Assert.AreEqual(typeof(ClassInitializeFixture).FullName, ClassInitializeFixture.Received.FullyQualifiedTestClassName);
    }

    [TestMethod]
    public async Task AssemblyInitialize_ReceivesATestContext()
    {
        AssemblyInitializeFixture.Received = null;

        await RunAsync(MakeTest(typeof(OrderFixture), nameof(OrderFixture.Test)));

        Assert.IsNotNull(AssemblyInitializeFixture.Received);
    }

    [TestMethod]
    public async Task MSTestCleanup_RunsDerivedFirst_InitializeRunsBaseFirst()
    {
        OrderBase.Log.Clear();

        await RunAsync(MakeTest(typeof(OrderFixture), nameof(OrderFixture.Test)));

        CollectionAssert.AreEqual(
            new[] { "base init", "derived init", "test", "derived cleanup", "base cleanup" },
            OrderBase.Log);
    }

    [TestMethod]
    public async Task NUnitTearDown_RunsDerivedFirst_SetUpRunsBaseFirst()
    {
        NUnitOrderBase.Log.Clear();

        await RunAsync(MakeTest(typeof(NUnitOrderFixture), nameof(NUnitOrderFixture.Test)));

        CollectionAssert.AreEqual(
            new[] { "base setup", "derived setup", "test", "derived teardown", "base teardown" },
            NUnitOrderBase.Log);
    }

    [TestMethod]
    public async Task ThrowingDerivedCleanup_StillRunsTheBaseCleanup()
    {
        ReleasingBase.Released = false;

        var result = await RunAsync(MakeTest(typeof(ThrowingCleanupFixture), nameof(ThrowingCleanupFixture.Test)));

        Assert.IsTrue(ReleasingBase.Released, "the base class's cleanup should run after a derived cleanup throws");
        Assert.AreEqual(1, result.Passed, "a cleanup failure is reported but does not fail the test");
    }

    [TestMethod]
    public async Task DataRows_AreDiscoveredAsOneTestEach_AndRunWithTheirArguments()
    {
        DataRowFixture.Seen.Clear();
        var tests = new TestDiscovery().Discover([typeof(DataRowFixture).Assembly.Location], nameof(DataRowFixture));

        var names = tests.Select(t => t.FullName[(typeof(DataRowFixture).FullName!.Length + 1)..]).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.AreEqual(5, names.Count, string.Join(" | ", names));
        Assert.AreEqual(2, names.Count(n => n.StartsWith(nameof(DataRowFixture.SignsIn) + " (", StringComparison.Ordinal)), string.Join(" | ", names));
        Assert.IsTrue(names.Any(n => n.StartsWith(nameof(DataRowFixture.Adds) + " (", StringComparison.Ordinal)), "[DataTestMethod] should be discovered");
        Assert.AreEqual(2, names.Count(n => n.StartsWith(nameof(DataRowFixture.Squares) + " (", StringComparison.Ordinal)), string.Join(" | ", names));
        Assert.AreEqual(names.Count, names.Distinct().Count(), "each row needs a name of its own");

        var result = await new TestRunner(1).RunAsync(tests, Reporter);

        Assert.AreEqual(5, result.Passed, $"failed: {result.Failed}");
        CollectionAssert.AreEquivalent(
            new[] { "role owner", "role staff", "adds 1+2", "square 2", "square 3" },
            DataRowFixture.Seen.ToArray());
    }

    [TestMethod]
    public void QuarantineList_NamesOneRow_OrEveryRowByTheMethod()
    {
        var rows = new TestDiscovery().Discover([typeof(DataRowFixture).Assembly.Location], $"{nameof(DataRowFixture)}.{nameof(DataRowFixture.SignsIn)}");
        var owner = rows.Single(t => t.FullName.Contains("owner"));
        var staff = rows.Single(t => t.FullName.Contains("staff"));

        var oneRow = new HashSet<string> { owner.FullName };
        Assert.IsTrue(RunCommand.IsListed(owner, oneRow));
        Assert.IsFalse(RunCommand.IsListed(staff, oneRow));

        var wholeMethod = new HashSet<string> { $"{typeof(DataRowFixture).FullName}.{nameof(DataRowFixture.SignsIn)}" };
        Assert.IsTrue(RunCommand.IsListed(owner, wholeMethod));
        Assert.IsTrue(RunCommand.IsListed(staff, wholeMethod));
    }

    private sealed class NullReporter : IReporter
    {
        public Task OnTestRunStartAsync(TestSuiteInfo suite) => Task.CompletedTask;
        public Task OnTestStartAsync(TestInfo test) => Task.CompletedTask;
        public Task OnTestEndAsync(TestInfo test, Motus.Abstractions.TestResult result) => Task.CompletedTask;
        public Task OnTestRunEndAsync(TestRunSummary summary) => Task.CompletedTask;
    }
}

/// <summary>
/// Data-driven tests that MSTest runs as well as <c>motus run</c>, so the two can be compared.
/// </summary>
[TestClass]
public class DataRowFixture
{
    public static ConcurrentBag<string> Seen { get; } = [];

    public static IEnumerable<object[]> SquareRows => [[2, 4], [3, 9]];

    [TestMethod]
    [DataRow("owner")]
    [DataRow("staff")]
    public void SignsIn(string role) => Seen.Add($"role {role}");

    [DataTestMethod]
    [DataRow(1, 2, 3)]
    public void Adds(int a, int b, int sum)
    {
        Assert.AreEqual(sum, a + b);
        Seen.Add($"adds {a}+{b}");
    }

    [TestMethod]
    [DynamicData(nameof(SquareRows))]
    public void Squares(int n, int square)
    {
        Assert.AreEqual(square, n * n);
        Seen.Add($"square {n}");
    }
}

/// <summary>
/// Not a [TestClass], so MSTest ignores it; <c>motus run</c> looks for <c>[AssemblyInitialize]</c>
/// on every exported type and runs it whenever these tests run a fixture from this assembly.
/// </summary>
public static class AssemblyInitializeFixture
{
    public static TestContext? Received { get; set; }

    [AssemblyInitialize]
    public static void Initialize(TestContext context) => Received = context;
}
