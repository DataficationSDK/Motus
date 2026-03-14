using System.Xml.Linq;
using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class JUnitReporterTests
{
    private string _outputPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _outputPath = Path.Combine(Path.GetTempPath(), $"motus-junit-{Guid.NewGuid()}.xml");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_outputPath))
            File.Delete(_outputPath);
    }

    [TestMethod]
    public async Task GeneratesValidXmlStructure()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestRunStartAsync(new TestSuiteInfo("Suite1", 1));
        var test = new TestInfo("Ns.PassingTest", "Suite1");
        await reporter.OnTestEndAsync(test, new TestResult("Ns.PassingTest", true, 100));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        var doc = XDocument.Load(_outputPath);
        var testSuites = doc.Root!;
        Assert.AreEqual("testsuites", testSuites.Name.LocalName);

        var testSuite = testSuites.Element("testsuite")!;
        Assert.AreEqual("Suite1", testSuite.Attribute("name")!.Value);
        Assert.AreEqual("1", testSuite.Attribute("tests")!.Value);

        var testCase = testSuite.Element("testcase")!;
        Assert.AreEqual("Ns.PassingTest", testCase.Attribute("name")!.Value);
    }

    [TestMethod]
    public async Task PassedTestHasNoFailureElement()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.Test1", "Suite1"),
            new TestResult("Ns.Test1", true, 50));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 50));

        var doc = XDocument.Load(_outputPath);
        var testCase = doc.Descendants("testcase").First();
        Assert.IsNull(testCase.Element("failure"));
    }

    [TestMethod]
    public async Task FailedTestIncludesFailureElement()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.FailTest", "Suite1"),
            new TestResult("Ns.FailTest", false, 200, "Assert failed", "at Ns.FailTest.Run()"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 0, 1, 0, 200));

        var doc = XDocument.Load(_outputPath);
        var failure = doc.Descendants("testcase").First().Element("failure")!;
        Assert.AreEqual("Assert failed", failure.Attribute("message")!.Value);
        Assert.IsTrue(failure.Value.Contains("at Ns.FailTest.Run()"));
    }

    [TestMethod]
    public async Task TestSuiteHasCorrectAttributes()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("T1", "S"),
            new TestResult("T1", true, 100));
        await reporter.OnTestEndAsync(
            new TestInfo("T2", "S"),
            new TestResult("T2", false, 200, "err"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 1, 2, 300));

        var doc = XDocument.Load(_outputPath);
        var suite = doc.Descendants("testsuite").First();
        Assert.AreEqual("S", suite.Attribute("name")!.Value);
        Assert.AreEqual("4", suite.Attribute("tests")!.Value);
        Assert.AreEqual("1", suite.Attribute("failures")!.Value);
        Assert.AreEqual("2", suite.Attribute("skipped")!.Value);
        Assert.AreEqual("0.300", suite.Attribute("time")!.Value);
    }

    [TestMethod]
    public async Task TestCaseHasClassnameAttribute()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.MyTest", "MySuite"),
            new TestResult("Ns.MyTest", true, 10));
        await reporter.OnTestRunEndAsync(new TestRunSummary("MySuite", 1, 0, 0, 10));

        var doc = XDocument.Load(_outputPath);
        var testCase = doc.Descendants("testcase").First();
        Assert.AreEqual("MySuite", testCase.Attribute("classname")!.Value);
    }

    [TestMethod]
    public async Task AttachmentsRenderedInSystemOut()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.Test1", "S"),
            new TestResult("Ns.Test1", true, 10, Attachments: ["/tmp/screenshot.png", "/tmp/trace.zip"]));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 10));

        var doc = XDocument.Load(_outputPath);
        var sysOut = doc.Descendants("testcase").First().Element("system-out")!;
        Assert.IsTrue(sysOut.Value.Contains("/tmp/screenshot.png"));
        Assert.IsTrue(sysOut.Value.Contains("/tmp/trace.zip"));
    }
}
