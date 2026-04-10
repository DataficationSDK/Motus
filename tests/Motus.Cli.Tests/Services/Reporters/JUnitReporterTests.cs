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

    [TestMethod]
    public async Task AccessibilityViolations_AddFailureElement()
    {
        var reporter = new JUnitReporter(_outputPath);

        var testInfo = new TestInfo("Ns.A11yTest", "Suite1");
        await reporter.OnTestEndAsync(testInfo, new TestResult("Ns.A11yTest", true, 100));
        await reporter.OnAccessibilityViolationAsync(
            new AccessibilityViolation("a11y-alt-text", AccessibilityViolationSeverity.Error,
                "Image missing alt text", null, null, null, "img.hero"),
            testInfo);
        await reporter.OnAccessibilityViolationAsync(
            new AccessibilityViolation("a11y-heading", AccessibilityViolationSeverity.Warning,
                "Bad heading hierarchy", null, null, null, null),
            testInfo);
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        var doc = XDocument.Load(_outputPath);
        var failure = doc.Descendants("testcase").First().Element("failure")!;
        Assert.AreEqual("accessibility", failure.Attribute("type")!.Value);
        Assert.AreEqual("2 accessibility violation(s)", failure.Attribute("message")!.Value);
        Assert.IsTrue(failure.Value.Contains("[Error] a11y-alt-text"));
        Assert.IsTrue(failure.Value.Contains("[Warning] a11y-heading"));
        Assert.IsTrue(failure.Value.Contains("(img.hero)"));
    }

    [TestMethod]
    public async Task NoAccessibilityViolations_NoFailureElement()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.CleanTest", "Suite1"),
            new TestResult("Ns.CleanTest", true, 50));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 50));

        var doc = XDocument.Load(_outputPath);
        Assert.IsNull(doc.Descendants("testcase").First().Element("failure"));
    }

    [TestMethod]
    public async Task PerformanceMetrics_AddPropertyElements()
    {
        var reporter = new JUnitReporter(_outputPath);

        var testInfo = new TestInfo("Ns.PerfTest", "Suite1");
        var metrics = new PerformanceMetrics(
            Lcp: 2345.6, Fcp: 1200.0, Ttfb: 150.0, Cls: 0.05, Inp: null,
            JsHeapSize: null, DomNodeCount: null, LayoutShifts: [], CollectedAtUtc: DateTime.UtcNow);

        await reporter.OnTestEndAsync(testInfo, new TestResult("Ns.PerfTest", true, 100));
        await reporter.OnPerformanceMetricsCollectedAsync(metrics, null, testInfo);
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        var doc = XDocument.Load(_outputPath);
        var props = doc.Descendants("testcase").First().Element("properties");
        Assert.IsNotNull(props, "Expected properties element for perf metrics.");

        var lcpProp = props!.Elements("property").FirstOrDefault(p => p.Attribute("name")?.Value == "perf.lcp");
        Assert.IsNotNull(lcpProp, "Expected perf.lcp property.");
        Assert.AreEqual("2345.6", lcpProp!.Attribute("value")!.Value);

        var fcpProp = props.Elements("property").FirstOrDefault(p => p.Attribute("name")?.Value == "perf.fcp");
        Assert.IsNotNull(fcpProp, "Expected perf.fcp property.");

        // INP is null so should not have a property
        var inpProp = props.Elements("property").FirstOrDefault(p => p.Attribute("name")?.Value == "perf.inp");
        Assert.IsNull(inpProp, "Null INP should not produce a property.");
    }

    [TestMethod]
    public async Task NoPerformanceMetrics_NoPropertiesElement()
    {
        var reporter = new JUnitReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.NoPerfTest", "Suite1"),
            new TestResult("Ns.NoPerfTest", true, 50));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 50));

        var doc = XDocument.Load(_outputPath);
        Assert.IsNull(doc.Descendants("testcase").First().Element("properties"));
    }
}
