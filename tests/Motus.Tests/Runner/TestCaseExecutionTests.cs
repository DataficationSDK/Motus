using System.Reflection;
using System.Runtime.Loader;
using Motus.Runner.Services.Execution;

namespace Motus.Tests.Runner;

/// <summary>
/// The pieces both runners share for running MSTest tests by reflection: data row expansion,
/// argument binding and the generated <c>TestContext</c>.
/// </summary>
[TestClass]
public class TestCaseExecutionTests
{
    public class Methods
    {
        public void Optional(int a, string b = "default") { }

        public void Params(int first, params int[] rest) { }

        public void Two(int a, int b) { }

        [TestMethod]
        [DataRow(1, DisplayName = "the first row")]
        public void Named(int value) { }

        [TestMethod]
        [DataRow(1)]
        [DataRow(1)]
        public void Duplicated(int value) { }

        [TestMethod]
        [DynamicData("NoSuchMember")]
        public void BrokenSource(int value) { }

        [TestMethod]
        public void Plain() { }
    }

    private static MethodInfo Method(string name) => typeof(Methods).GetMethod(name)!;

    [TestMethod]
    public void BindArguments_FillsOmittedOptionalParameters()
    {
        var args = TestCaseInvoker.BindArguments(Method(nameof(Methods.Optional)), [1])!;

        Assert.AreEqual(2, args.Length);
        Assert.AreEqual(1, args[0]);
        Assert.AreSame(Type.Missing, args[1]);
    }

    [TestMethod]
    public void BindArguments_GathersTrailingValuesIntoParams()
    {
        var args = TestCaseInvoker.BindArguments(Method(nameof(Methods.Params)), [1, 2, 3])!;

        Assert.AreEqual(2, args.Length);
        CollectionAssert.AreEqual(new[] { 2, 3 }, (int[])args[1]!);
    }

    [TestMethod]
    public void BindArguments_ParamsWithNoTrailingValues_IsAnEmptyArray()
    {
        var args = TestCaseInvoker.BindArguments(Method(nameof(Methods.Params)), [1])!;

        Assert.AreEqual(0, ((int[])args[1]!).Length);
    }

    [TestMethod]
    public void BindArguments_MissingRequiredParameters_SaysHowToSupplyThem()
    {
        var ex = Assert.ThrowsException<InvalidOperationException>(
            () => TestCaseInvoker.BindArguments(Method(nameof(Methods.Two)), null));

        StringAssert.Contains(ex.Message, "[DataRow]");
    }

    [TestMethod]
    public void Expand_UsesTheRowsDisplayName_KeepingTheMethodName()
    {
        var cases = TestCaseExpander.Expand(typeof(Methods), Method(nameof(Methods.Named)));

        Assert.AreEqual($"{typeof(Methods).FullName}.Named (the first row)", cases.Single().FullName);
    }

    [TestMethod]
    public void Expand_IdenticalRows_GetDistinctNames()
    {
        var cases = TestCaseExpander.Expand(typeof(Methods), Method(nameof(Methods.Duplicated)));

        Assert.AreEqual(2, cases.Count);
        Assert.AreNotEqual(cases[0].FullName, cases[1].FullName);
    }

    [TestMethod]
    public void Expand_UnreadableDataSource_ReportsIt_AndLeavesOneCase()
    {
        var errors = new List<string>();

        var cases = TestCaseExpander.Expand(typeof(Methods), Method(nameof(Methods.BrokenSource)), errors.Add);

        Assert.AreEqual(1, errors.Count);
        Assert.AreEqual($"{typeof(Methods).FullName}.BrokenSource", cases.Single().FullName);
        Assert.IsNull(cases.Single().Arguments);
    }

    [TestMethod]
    public void Expand_MethodWithoutData_IsOneCaseWithoutArguments()
    {
        var cases = TestCaseExpander.Expand(typeof(Methods), Method(nameof(Methods.Plain)));

        Assert.AreEqual($"{typeof(Methods).FullName}.Plain", cases.Single().FullName);
        Assert.IsNull(cases.Single().Arguments);
    }

    [TestMethod]
    public void TestContext_ReflectsTheOutcomeAsItChanges()
    {
        var testCase = new TestCaseContext(typeof(Methods), Method(nameof(Methods.Plain)));

        var context = (TestContext)MSTestContextFactory.TryCreate(typeof(TestContext), testCase)!;

        Assert.AreEqual(UnitTestOutcome.InProgress, context.CurrentTestOutcome);
        testCase.Outcome = TestCaseOutcome.Passed;
        Assert.AreEqual(UnitTestOutcome.Passed, context.CurrentTestOutcome);
        testCase.Outcome = TestCaseOutcome.Failed;
        Assert.AreEqual(UnitTestOutcome.Failed, context.CurrentTestOutcome);
    }

    [TestMethod]
    public void TestContext_DescribesTheTest()
    {
        var testCase = new TestCaseContext(typeof(Methods), Method(nameof(Methods.Plain)));

        var context = (TestContext)MSTestContextFactory.TryCreate(typeof(TestContext), testCase)!;

        Assert.AreEqual(nameof(Methods.Plain), context.TestName);
        Assert.AreEqual(typeof(Methods).FullName, context.FullyQualifiedTestClassName);
        Assert.IsNotNull(context.CancellationTokenSource);
        Assert.IsTrue(Directory.Exists(context.TestRunDirectory));
        Assert.AreSame(context.Properties, context.Properties);

        // Writes go to the console and must not throw, with or without format arguments.
        context.WriteLine("plain");
        context.WriteLine("formatted {0}", 1);
        context.Write("partial");
        context.AddResultFile("/nowhere/file.txt");
    }

    [TestMethod]
    public void TestContext_FromMSTest4_IsBuiltToo()
    {
        // MSTest 4 moved TestContext to another assembly and made more of it abstract. Load it the
        // way the runners meet it, from a test assembly's own folder, beside this project's MSTest 3.
        var root = typeof(TestCaseExecutionTests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NuGetPackageRoot")?.Value ?? "";
        var package = Path.Combine(root, "mstest.testframework", "4.0.1");
        var framework = Path.Combine(package, "lib", "net8.0", "MSTest.TestFramework.dll");
        var extensions = Path.Combine(package, "buildTransitive", "net8.0", "MSTest.TestFramework.Extensions.dll");
        if (!File.Exists(framework) || !File.Exists(extensions))
            Assert.Inconclusive($"MSTest 4 was not restored to {package}.");

        var loadContext = new AssemblyLoadContext("mstest4", isCollectible: true);
        loadContext.Resolving += (alc, name) => name.Name == "MSTest.TestFramework" ? alc.LoadFromAssemblyPath(framework) : null;
        try
        {
            var contextType = loadContext.LoadFromAssemblyPath(extensions).GetType(MSTestContextFactory.TestContextTypeName, throwOnError: true)!;
            Assert.AreNotEqual(typeof(TestContext).Assembly.GetName().Name, contextType.Assembly.GetName().Name);
            var testCase = new TestCaseContext(typeof(Methods), Method(nameof(Methods.Plain)), "Plain (1)", [1]);

            var context = MSTestContextFactory.TryCreate(contextType, testCase);

            Assert.IsNotNull(context, "an MSTest 4 TestContext should be built");
            Assert.AreEqual(nameof(Methods.Plain), contextType.GetProperty("TestName")!.GetValue(context));
            Assert.AreEqual("Plain (1)", contextType.GetProperty("TestDisplayName")!.GetValue(context));
            testCase.Outcome = TestCaseOutcome.Passed;
            Assert.AreEqual("Passed", contextType.GetProperty("CurrentTestOutcome")!.GetValue(context)!.ToString());
            contextType.GetMethod("WriteLine", [typeof(string)])!.Invoke(context, ["hello from MSTest 4"]);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [TestMethod]
    public void TryCreate_ForATypeThatIsNotTestContext_ReturnsNull()
    {
        var testCase = new TestCaseContext(typeof(Methods), null);

        Assert.IsNull(MSTestContextFactory.TryCreate(typeof(Stream), testCase));
    }
}
