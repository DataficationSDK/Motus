using System.Xml.Linq;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class TrxReporterTests
{
    private static readonly XNamespace TrxNs = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private string _outputPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _outputPath = Path.Combine(Path.GetTempPath(), $"motus-trx-{Guid.NewGuid()}.trx");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_outputPath))
            File.Delete(_outputPath);
    }

    [TestMethod]
    public async Task GeneratesValidTrxStructure()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.Test1", "Suite1"),
            new TestResult("Ns.Test1", true, 100));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        var doc = XDocument.Load(_outputPath);
        var root = doc.Root!;
        Assert.AreEqual("TestRun", root.Name.LocalName);
        Assert.AreEqual(TrxNs.NamespaceName, root.Name.NamespaceName);
        Assert.IsNotNull(root.Element(TrxNs + "Results"));
        Assert.IsNotNull(root.Element(TrxNs + "TestDefinitions"));
        Assert.IsNotNull(root.Element(TrxNs + "TestEntries"));
        Assert.IsNotNull(root.Element(TrxNs + "ResultSummary"));
    }

    [TestMethod]
    public async Task PassedTestHasCorrectOutcome()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.Test1", "S"),
            new TestResult("Ns.Test1", true, 50));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 50));

        var doc = XDocument.Load(_outputPath);
        var result = doc.Descendants(TrxNs + "UnitTestResult").First();
        Assert.AreEqual("Passed", result.Attribute("outcome")!.Value);
    }

    [TestMethod]
    public async Task FailedTestHasCorrectOutcomeAndError()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.FailTest", "S"),
            new TestResult("Ns.FailTest", false, 200, "Assert failed", "at Ns.FailTest()"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 0, 1, 0, 200));

        var doc = XDocument.Load(_outputPath);
        var result = doc.Descendants(TrxNs + "UnitTestResult").First();
        Assert.AreEqual("Failed", result.Attribute("outcome")!.Value);

        var errorInfo = result.Descendants(TrxNs + "ErrorInfo").First();
        Assert.AreEqual("Assert failed", errorInfo.Element(TrxNs + "Message")!.Value);
        Assert.AreEqual("at Ns.FailTest()", errorInfo.Element(TrxNs + "StackTrace")!.Value);
    }

    [TestMethod]
    public async Task DurationFormattedCorrectly()
    {
        var reporter = new TrxReporter(_outputPath);

        // 1500ms = 00:00:01.5000000
        await reporter.OnTestEndAsync(
            new TestInfo("Ns.Test1", "S"),
            new TestResult("Ns.Test1", true, 1500));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 1500));

        var doc = XDocument.Load(_outputPath);
        var result = doc.Descendants(TrxNs + "UnitTestResult").First();
        var duration = result.Attribute("duration")!.Value;
        Assert.AreEqual("00:00:01.5000000", duration);
    }

    [TestMethod]
    public async Task CounterTotalsMatch()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("T1", "S"),
            new TestResult("T1", true, 10));
        await reporter.OnTestEndAsync(
            new TestInfo("T2", "S"),
            new TestResult("T2", false, 20, "err"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 1, 2, 30));

        var doc = XDocument.Load(_outputPath);
        var counters = doc.Descendants(TrxNs + "Counters").First();
        Assert.AreEqual("4", counters.Attribute("total")!.Value);
        Assert.AreEqual("1", counters.Attribute("passed")!.Value);
        Assert.AreEqual("1", counters.Attribute("failed")!.Value);
        Assert.AreEqual("2", counters.Attribute("notExecuted")!.Value);
    }

    [TestMethod]
    public async Task TestDefinitionsContainTestMethod()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("MyNamespace.MyClass.TestMethod", "Suite1"),
            new TestResult("MyNamespace.MyClass.TestMethod", true, 10));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 10));

        var doc = XDocument.Load(_outputPath);
        var unitTest = doc.Descendants(TrxNs + "UnitTest").First();
        Assert.AreEqual("MyNamespace.MyClass.TestMethod", unitTest.Attribute("name")!.Value);

        var testMethod = unitTest.Element(TrxNs + "TestMethod")!;
        Assert.AreEqual("MyNamespace.MyClass", testMethod.Attribute("className")!.Value);
        Assert.AreEqual("TestMethod", testMethod.Attribute("name")!.Value);
    }

    [TestMethod]
    public async Task ResultSummaryOutcomeReflectsFailures()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("T1", "S"),
            new TestResult("T1", false, 10, "err"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 0, 1, 0, 10));

        var doc = XDocument.Load(_outputPath);
        var summary = doc.Descendants(TrxNs + "ResultSummary").First();
        Assert.AreEqual("Failed", summary.Attribute("outcome")!.Value);
    }

    [TestMethod]
    public async Task ResultSummaryCompletedWhenAllPass()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("T1", "S"),
            new TestResult("T1", true, 10));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 10));

        var doc = XDocument.Load(_outputPath);
        var summary = doc.Descendants(TrxNs + "ResultSummary").First();
        Assert.AreEqual("Completed", summary.Attribute("outcome")!.Value);
    }

    [TestMethod]
    public async Task AccessibilityViolation_AddsA11yCategory()
    {
        var reporter = new TrxReporter(_outputPath);

        var testInfo = new TestInfo("Ns.A11yTest", "Suite1");
        await reporter.OnTestEndAsync(testInfo, new TestResult("Ns.A11yTest", true, 100));
        await reporter.OnAccessibilityViolationAsync(
            new AccessibilityViolation("a11y-alt-text", AccessibilityViolationSeverity.Error,
                "Missing alt text", null, null, null, null),
            testInfo);
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        var doc = XDocument.Load(_outputPath);
        var unitTest = doc.Descendants(TrxNs + "UnitTest").First();
        var category = unitTest.Element(TrxNs + "TestCategory");
        Assert.IsNotNull(category, "Test with violations should have a TestCategory element.");
        var item = category!.Element(TrxNs + "TestCategoryItem");
        Assert.AreEqual("a11y", item!.Attribute("TestCategory")!.Value);
    }

    [TestMethod]
    public async Task NoAccessibilityViolation_NoCategory()
    {
        var reporter = new TrxReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.CleanTest", "Suite1"),
            new TestResult("Ns.CleanTest", true, 50));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 50));

        var doc = XDocument.Load(_outputPath);
        var unitTest = doc.Descendants(TrxNs + "UnitTest").First();
        Assert.IsNull(unitTest.Element(TrxNs + "TestCategory"),
            "Test without violations should not have a TestCategory element.");
    }
}
