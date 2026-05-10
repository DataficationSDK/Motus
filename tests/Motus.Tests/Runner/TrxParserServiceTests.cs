using System.Xml.Linq;
using Motus.Runner.Services;
using Motus.Runner.Services.Models;

namespace Motus.Tests.Runner;

[TestClass]
public class TrxParserServiceTests
{
    private static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";
    private string _tempFile = "";

    [TestInitialize]
    public void Setup()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"motus-trx-test-{Guid.NewGuid()}.trx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }

    [TestMethod]
    public void ParseFile_AllPassed_ReturnsCorrectCounts()
    {
        WriteTrx(BuildTestRun(
            ("Ns.Cls.Test1", "Passed", "00:00:00.0500000", "Test.dll", null, null, null),
            ("Ns.Cls.Test2", "Passed", "00:00:00.0750000", "Test.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(2, result.Tests.Count);
        Assert.AreEqual(2, result.States.Count);
        Assert.AreEqual(TestStatus.Passed, result.States["Ns.Cls.Test1"].Status);
        Assert.AreEqual(TestStatus.Passed, result.States["Ns.Cls.Test2"].Status);
        Assert.AreEqual(2, result.Summary.Passed);
        Assert.AreEqual(0, result.Summary.Failed);
        Assert.AreEqual(0, result.Summary.Skipped);
    }

    [TestMethod]
    public void ParseFile_AllFailed_ExtractsErrorAndStackTrace()
    {
        WriteTrx(BuildTestRun(
            ("Ns.Cls.TestA", "Failed", "00:00:00.1000000", "Test.dll", "AssertFailed", "at line 42", null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(1, result.Tests.Count);
        var state = result.States["Ns.Cls.TestA"];
        Assert.AreEqual(TestStatus.Failed, state.Status);
        Assert.AreEqual("AssertFailed", state.ErrorMessage);
        Assert.AreEqual("at line 42", state.StackTrace);
        Assert.AreEqual(1, result.Summary.Failed);
    }

    [TestMethod]
    public void ParseFile_OutcomeMapping_PassedFailedNotExecuted()
    {
        WriteTrx(BuildTestRun(
            ("A.B.Pass", "Passed", "00:00:00.0100000", "Test.dll", null, null, null),
            ("A.B.Fail", "Failed", "00:00:00.0200000", "Test.dll", "boom", null, null),
            ("A.B.Skip", "NotExecuted", "00:00:00.0000000", "Test.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(TestStatus.Passed, result.States["A.B.Pass"].Status);
        Assert.AreEqual(TestStatus.Failed, result.States["A.B.Fail"].Status);
        Assert.AreEqual(TestStatus.Skipped, result.States["A.B.Skip"].Status);
    }

    [TestMethod]
    public void ParseFile_NullableReflection_TypeAndMethodAreNull()
    {
        WriteTrx(BuildTestRun(
            ("Ns.C.M", "Passed", "00:00:00.0100000", "Test.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        var test = result.Tests.Single();
        Assert.IsNull(test.TestClass);
        Assert.IsNull(test.TestMethod);
        Assert.AreEqual("Ns.C.M", test.FullName);
    }

    [TestMethod]
    public void ParseFile_AssemblyNameDerivedFromCodeBase()
    {
        WriteTrx(BuildTestRun(
            ("Ns.C.M", "Passed", "00:00:00.0100000", "MyTests.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual("MyTests", result.Tests.Single().AssemblyName);
    }

    [TestMethod]
    public void ParseFile_TestCategoriesExtracted()
    {
        WriteTrx(BuildTestRun(
            ("Ns.C.M", "Failed", "00:00:00.0100000", "Test.dll", "x", null, new[] { "a11y", "slow" })));

        var result = new TrxParserService().ParseFile(_tempFile);

        var test = result.Tests.Single();
        CollectionAssert.AreEqual(new[] { "a11y", "slow" }, test.Categories?.ToList());
    }

    [TestMethod]
    public void ParseFile_MissingOptionalElements_ReturnsNullsGracefully()
    {
        WriteTrx(BuildTestRun(
            ("Ns.C.M", "Passed", "00:00:00.0100000", "Test.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        var state = result.States.Single().Value;
        Assert.IsNull(state.ErrorMessage);
        Assert.IsNull(state.StackTrace);
        var test = result.Tests.Single();
        Assert.IsNull(test.Categories);
    }

    [TestMethod]
    public void ParseFile_SummaryCountsMatchCountersElement()
    {
        WriteTrx(BuildTestRun(
            counters: (total: 5, executed: 4, passed: 3, failed: 1, notExecuted: 1),
            tests:
            [
                ("A.B.T1", "Passed", "00:00:00.0100000", "Test.dll", null, null, null),
                ("A.B.T2", "Passed", "00:00:00.0100000", "Test.dll", null, null, null),
                ("A.B.T3", "Passed", "00:00:00.0100000", "Test.dll", null, null, null),
                ("A.B.T4", "Failed", "00:00:00.0100000", "Test.dll", "fail", null, null),
                ("A.B.T5", "NotExecuted", "00:00:00.0000000", "Test.dll", null, null, null),
            ]));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(5, result.Summary.Total);
        Assert.AreEqual(3, result.Summary.Passed);
        Assert.AreEqual(1, result.Summary.Failed);
        Assert.AreEqual(1, result.Summary.Skipped);
    }

    [TestMethod]
    public void ParseFile_DurationAccumulatedFromResults()
    {
        WriteTrx(BuildTestRun(
            ("A.B.T1", "Passed", "00:00:00.5000000", "Test.dll", null, null, null),
            ("A.B.T2", "Passed", "00:00:00.2500000", "Test.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(750, result.Summary.Duration.TotalMilliseconds, 0.5);
    }

    [TestMethod]
    public void ParseFile_MinimalTrx_NoOptionalChildren_DoesNotThrow()
    {
        // Bare-bones TRX: required <Results> + <UnitTestResult>, no <TestDefinitions>, no <Output>, no <ResultSummary>
        var doc = new XDocument(new XElement(Ns + "TestRun",
            new XAttribute("id", Guid.NewGuid()),
            new XAttribute("name", "Bare"),
            new XElement(Ns + "Results",
                new XElement(Ns + "UnitTestResult",
                    new XAttribute("testId", Guid.NewGuid()),
                    new XAttribute("testName", "X.Y.Z"),
                    new XAttribute("outcome", "Passed"),
                    new XAttribute("duration", "00:00:00.0100000")))));
        File.WriteAllText(_tempFile, doc.ToString());

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(1, result.Tests.Count);
        Assert.AreEqual(TestStatus.Passed, result.States["X.Y.Z"].Status);
    }

    [TestMethod]
    public void ParseFile_FileNameStoredInSummary()
    {
        WriteTrx(BuildTestRun(
            ("Ns.C.M", "Passed", "00:00:00.0100000", "Test.dll", null, null, null)));

        var result = new TrxParserService().ParseFile(_tempFile);

        Assert.AreEqual(Path.GetFileName(_tempFile), result.Summary.FileName);
    }

    private void WriteTrx(XDocument doc) => File.WriteAllText(_tempFile, doc.ToString());

    private static XDocument BuildTestRun(params (string FullName, string Outcome, string Duration, string CodeBase, string? ErrorMessage, string? StackTrace, string[]? Categories)[] tests)
        => BuildTestRun(counters: null, tests: tests);

    private static XDocument BuildTestRun(
        (int total, int executed, int passed, int failed, int notExecuted)? counters,
        (string FullName, string Outcome, string Duration, string CodeBase, string? ErrorMessage, string? StackTrace, string[]? Categories)[] tests)
    {
        var results = new XElement(Ns + "Results");
        var definitions = new XElement(Ns + "TestDefinitions");
        int passedCount = 0, failedCount = 0, skippedCount = 0;

        foreach (var (fullName, outcome, duration, codeBase, errorMessage, stackTrace, categories) in tests)
        {
            var testId = Guid.NewGuid();
            var executionId = Guid.NewGuid();

            var unitTestResult = new XElement(Ns + "UnitTestResult",
                new XAttribute("executionId", executionId),
                new XAttribute("testId", testId),
                new XAttribute("testName", fullName),
                new XAttribute("duration", duration),
                new XAttribute("outcome", outcome));

            if (errorMessage is not null || stackTrace is not null)
            {
                var errorInfo = new XElement(Ns + "ErrorInfo");
                if (errorMessage is not null) errorInfo.Add(new XElement(Ns + "Message", errorMessage));
                if (stackTrace is not null) errorInfo.Add(new XElement(Ns + "StackTrace", stackTrace));
                unitTestResult.Add(new XElement(Ns + "Output", errorInfo));
            }

            results.Add(unitTestResult);

            switch (outcome)
            {
                case "Passed": passedCount++; break;
                case "Failed": failedCount++; break;
                default: skippedCount++; break;
            }

            var lastDot = fullName.LastIndexOf('.');
            var className = lastDot > 0 ? fullName[..lastDot] : fullName;
            var methodName = lastDot > 0 && lastDot + 1 < fullName.Length ? fullName[(lastDot + 1)..] : fullName;

            var unitTest = new XElement(Ns + "UnitTest",
                new XAttribute("name", fullName),
                new XAttribute("id", testId),
                new XElement(Ns + "TestMethod",
                    new XAttribute("codeBase", codeBase),
                    new XAttribute("className", className),
                    new XAttribute("name", methodName)));

            if (categories is not null)
            {
                var category = new XElement(Ns + "TestCategory");
                foreach (var cat in categories)
                {
                    category.Add(new XElement(Ns + "TestCategoryItem", new XAttribute("TestCategory", cat)));
                }
                unitTest.Add(category);
            }

            definitions.Add(unitTest);
        }

        var totals = counters ?? (
            total: tests.Length,
            executed: passedCount + failedCount,
            passed: passedCount,
            failed: failedCount,
            notExecuted: skippedCount);

        var resultSummary = new XElement(Ns + "ResultSummary",
            new XAttribute("outcome", totals.failed > 0 ? "Failed" : "Completed"),
            new XElement(Ns + "Counters",
                new XAttribute("total", totals.total),
                new XAttribute("executed", totals.executed),
                new XAttribute("passed", totals.passed),
                new XAttribute("failed", totals.failed),
                new XAttribute("notExecuted", totals.notExecuted)));

        var testRun = new XElement(Ns + "TestRun",
            new XAttribute("id", Guid.NewGuid()),
            new XAttribute("name", "Test Suite"),
            results,
            definitions,
            resultSummary);

        return new XDocument(testRun);
    }
}
