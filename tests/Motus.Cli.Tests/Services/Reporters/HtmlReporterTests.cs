using Motus.Abstractions;
using Motus.Cli.Services.Reporters;
using TestResult = Motus.Abstractions.TestResult;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class HtmlReporterTests
{
    private string _outputPath = null!;

    [TestInitialize]
    public void Setup()
    {
        _outputPath = Path.Combine(Path.GetTempPath(), $"motus-html-{Guid.NewGuid()}.html");
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_outputPath))
            File.Delete(_outputPath);
    }

    [TestMethod]
    public async Task GeneratesSelfContainedHtml()
    {
        var reporter = new HtmlReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.Test1", "Suite1"),
            new TestResult("Ns.Test1", true, 100));
        await reporter.OnTestRunEndAsync(new TestRunSummary("Suite1", 1, 0, 0, 100));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("<!DOCTYPE html>"));
        Assert.IsTrue(html.Contains("<style>"));
        Assert.IsFalse(html.Contains("<link rel=\"stylesheet\""));
        Assert.IsFalse(html.Contains("<script src="));
    }

    [TestMethod]
    public async Task ContainsTestNameAndStatus()
    {
        var reporter = new HtmlReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.MyPassingTest", "S"),
            new TestResult("Ns.MyPassingTest", true, 42));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 42));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("Ns.MyPassingTest"));
        Assert.IsTrue(html.Contains("PASS"));
        Assert.IsTrue(html.Contains("42ms"));
    }

    [TestMethod]
    public async Task FailedTestShowsErrorMessage()
    {
        var reporter = new HtmlReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.FailTest", "S"),
            new TestResult("Ns.FailTest", false, 50, "Expected true but got false"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 0, 1, 0, 50));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("FAIL"));
        Assert.IsTrue(html.Contains("Expected true but got false"));
    }

    [TestMethod]
    public async Task FailedTestHasCollapsibleDetails()
    {
        var reporter = new HtmlReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.FailTest", "S"),
            new TestResult("Ns.FailTest", false, 50, "err"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 0, 1, 0, 50));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("<details>"));
        Assert.IsTrue(html.Contains("<summary>"));
    }

    [TestMethod]
    public async Task StackTraceRenderedInOutput()
    {
        var reporter = new HtmlReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("Ns.FailTest", "S"),
            new TestResult("Ns.FailTest", false, 50, "err", "at Ns.FailTest.Run() line 42"));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 0, 1, 0, 50));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("stack-trace"));
        Assert.IsTrue(html.Contains("at Ns.FailTest.Run() line 42"));
    }

    [TestMethod]
    public async Task ScreenshotAttachmentEmbeddedAsBase64()
    {
        // Create a tiny PNG file for testing
        var pngPath = Path.Combine(Path.GetTempPath(), $"motus-test-{Guid.NewGuid()}.png");
        try
        {
            // Minimal valid PNG (1x1 pixel, white)
            var pngBytes = Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==");
            await File.WriteAllBytesAsync(pngPath, pngBytes);

            var reporter = new HtmlReporter(_outputPath);

            await reporter.OnTestEndAsync(
                new TestInfo("Ns.Test1", "S"),
                new TestResult("Ns.Test1", true, 10, Attachments: [pngPath]));
            await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 10));

            var html = await File.ReadAllTextAsync(_outputPath);
            Assert.IsTrue(html.Contains("data:image/png;base64,"));
            Assert.IsTrue(html.Contains("<img "));
        }
        finally
        {
            if (File.Exists(pngPath))
                File.Delete(pngPath);
        }
    }

    [TestMethod]
    public async Task SummaryBarShowsCounts()
    {
        var reporter = new HtmlReporter(_outputPath);

        await reporter.OnTestEndAsync(
            new TestInfo("T1", "S"),
            new TestResult("T1", true, 10));
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 5, 2, 1, 1000));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("5 Passed"));
        Assert.IsTrue(html.Contains("2 Failed"));
        Assert.IsTrue(html.Contains("1 Skipped"));
        Assert.IsTrue(html.Contains("8 Total"));
    }

    [TestMethod]
    public async Task AccessibilityViolations_RenderedInTestDetails()
    {
        var reporter = new HtmlReporter(_outputPath);

        var testInfo = new TestInfo("Ns.A11yTest", "S");
        await reporter.OnTestEndAsync(testInfo, new TestResult("Ns.A11yTest", true, 100));
        await reporter.OnAccessibilityViolationAsync(
            new AccessibilityViolation("a11y-alt-text", AccessibilityViolationSeverity.Error,
                "Image missing alt text", null, null, null, "img.hero"),
            testInfo);
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 100));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("Accessibility Violations"),
            $"Expected accessibility heading, got: {html[..200]}...");
        Assert.IsTrue(html.Contains("a11y-alt-text"), "Expected rule ID in HTML.");
        Assert.IsTrue(html.Contains("img.hero"), "Expected selector in HTML.");
        Assert.IsTrue(html.Contains("violation error"), "Expected error severity CSS class.");
        Assert.IsTrue(html.Contains("<details>"),
            "Test with violations should have collapsible details.");
    }

    [TestMethod]
    public async Task AccessibilityViolation_Warning_HasCorrectSeverityClass()
    {
        var reporter = new HtmlReporter(_outputPath);

        var testInfo = new TestInfo("Ns.WarnTest", "S");
        await reporter.OnTestEndAsync(testInfo, new TestResult("Ns.WarnTest", true, 50));
        await reporter.OnAccessibilityViolationAsync(
            new AccessibilityViolation("a11y-heading", AccessibilityViolationSeverity.Warning,
                "Bad heading hierarchy", null, null, null, null),
            testInfo);
        await reporter.OnTestRunEndAsync(new TestRunSummary("S", 1, 0, 0, 50));

        var html = await File.ReadAllTextAsync(_outputPath);
        Assert.IsTrue(html.Contains("violation warning"), "Expected warning severity CSS class.");
    }
}
