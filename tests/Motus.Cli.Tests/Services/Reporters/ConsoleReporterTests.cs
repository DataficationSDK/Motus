using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class ConsoleReporterTests
{
    [TestMethod]
    public async Task OnTestRunStart_WritesHeader()
    {
        var sw = new StringWriter();
        var reporter = new ConsoleReporter(sw, useColor: false);

        await reporter.OnTestRunStartAsync(new TestSuiteInfo("Suite1", 7));

        var output = sw.ToString();
        Assert.IsTrue(output.Contains("Running 7 test(s)..."), $"Expected header, got: {output}");
    }

    [TestMethod]
    public async Task OnTestEnd_Passed_WritesPASS()
    {
        var sw = new StringWriter();
        var reporter = new ConsoleReporter(sw, useColor: false);

        var test = new TestInfo("MyNamespace.MyTest", "Suite1");
        var result = new TestResult("MyNamespace.MyTest", true, 123.4);

        await reporter.OnTestEndAsync(test, result);

        var output = sw.ToString();
        Assert.IsTrue(output.Contains("[PASS]"), $"Expected [PASS], got: {output}");
        Assert.IsTrue(output.Contains("MyNamespace.MyTest"), $"Expected test name, got: {output}");
        Assert.IsTrue(output.Contains("123ms"), $"Expected duration, got: {output}");
    }

    [TestMethod]
    public async Task OnTestEnd_Failed_WritesFAIL()
    {
        var sw = new StringWriter();
        var reporter = new ConsoleReporter(sw, useColor: false);

        var test = new TestInfo("MyNamespace.FailingTest", "Suite1");
        var result = new TestResult("MyNamespace.FailingTest", false, 50, "Something broke");

        await reporter.OnTestEndAsync(test, result);

        var output = sw.ToString();
        Assert.IsTrue(output.Contains("[FAIL]"), $"Expected [FAIL], got: {output}");
        Assert.IsTrue(output.Contains("Something broke"), $"Expected error message, got: {output}");
    }

    [TestMethod]
    public async Task OnTestRunEnd_WritesSummary()
    {
        var sw = new StringWriter();
        var reporter = new ConsoleReporter(sw, useColor: false);

        var summary = new TestRunSummary("Suite1", 8, 2, 1, 5500);

        await reporter.OnTestRunEndAsync(summary);

        var output = sw.ToString();
        Assert.IsTrue(output.Contains("8 passed"), $"Expected passed count, got: {output}");
        Assert.IsTrue(output.Contains("2 failed"), $"Expected failed count, got: {output}");
        Assert.IsTrue(output.Contains("11 total"), $"Expected total count, got: {output}");
        Assert.IsTrue(output.Contains("5.5s"), $"Expected duration, got: {output}");
    }

    [TestMethod]
    public async Task OnAccessibilityViolation_Error_PrintsInline()
    {
        var sw = new StringWriter();
        var reporter = new ConsoleReporter(sw, useColor: false);

        var violation = new AccessibilityViolation(
            "a11y-alt-text", AccessibilityViolationSeverity.Error,
            "Image missing alternative text", null, null, null, "img.hero");
        var test = new TestInfo("MyTest", "Suite1");

        await reporter.OnAccessibilityViolationAsync(violation, test);

        var output = sw.ToString();
        Assert.IsTrue(output.Contains("[A11Y Error]"), $"Expected severity label, got: {output}");
        Assert.IsTrue(output.Contains("a11y-alt-text"), $"Expected rule ID, got: {output}");
        Assert.IsTrue(output.Contains("Image missing alternative text"), $"Expected message, got: {output}");
        Assert.IsTrue(output.Contains("(img.hero)"), $"Expected selector, got: {output}");
    }

    [TestMethod]
    public async Task OnAccessibilityViolation_Warning_PrintsYellowLabel()
    {
        var sw = new StringWriter();
        var reporter = new ConsoleReporter(sw, useColor: false);

        var violation = new AccessibilityViolation(
            "a11y-heading", AccessibilityViolationSeverity.Warning,
            "Heading levels should increase by one", null, null, null, null);
        var test = new TestInfo("MyTest", "Suite1");

        await reporter.OnAccessibilityViolationAsync(violation, test);

        var output = sw.ToString();
        Assert.IsTrue(output.Contains("[A11Y Warning]"), $"Expected warning label, got: {output}");
        Assert.IsFalse(output.Contains("("), "No selector should omit parentheses.");
    }
}
