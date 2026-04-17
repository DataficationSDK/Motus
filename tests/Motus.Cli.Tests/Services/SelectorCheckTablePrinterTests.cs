using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class SelectorCheckTablePrinterTests
{
    private static SelectorCheckResult Make(
        SelectorCheckStatus status,
        string selector = "#submit",
        string method = "Locator",
        int matches = 0,
        string? suggestion = null,
        string? note = null)
        => new(status, selector, method, "/src/Login.cs", 12, "https://example.com", matches, suggestion, note);

    [TestMethod]
    public void Print_HealthyResult_NoColor_ContainsHealthyStatus()
    {
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(new[] { Make(SelectorCheckStatus.Healthy, matches: 1) }, writer, useColor: false);

        var output = writer.ToString();
        StringAssert.Contains(output, "HEALTHY");
        Assert.IsFalse(output.Contains("\x1b["), "Expected no ANSI escapes when useColor is false");
    }

    [TestMethod]
    public void Print_BrokenResult_WithColor_UsesRedEscape()
    {
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(new[] { Make(SelectorCheckStatus.Broken) }, writer, useColor: true);

        var output = writer.ToString();
        StringAssert.Contains(output, "BROKEN");
        StringAssert.Contains(output, "\x1b[31m");
    }

    [TestMethod]
    public void Print_AmbiguousResult_WithColor_UsesYellowEscape()
    {
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(
            new[] { Make(SelectorCheckStatus.Ambiguous, matches: 3) }, writer, useColor: true);

        var output = writer.ToString();
        StringAssert.Contains(output, "AMBIGUOUS");
        StringAssert.Contains(output, "\x1b[33m");
    }

    [TestMethod]
    public void Print_BrokenWithSuggestion_EmitsSuggestionLine()
    {
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(
            new[] { Make(SelectorCheckStatus.Broken, suggestion: "GetByTestId(\"new\")") },
            writer, useColor: false);

        var output = writer.ToString();
        StringAssert.Contains(output, "-> Suggestion: GetByTestId(\"new\")");
    }

    [TestMethod]
    public void Print_NoSuggestion_NoSuggestionLine()
    {
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(
            new[] { Make(SelectorCheckStatus.Healthy, matches: 1) },
            writer, useColor: false);

        Assert.IsFalse(writer.ToString().Contains("-> Suggestion"));
    }

    [TestMethod]
    public void Print_LongSelector_IsTruncated()
    {
        var longSelector = new string('x', 200);
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(
            new[] { Make(SelectorCheckStatus.Healthy, selector: longSelector, matches: 1) },
            writer, useColor: false);

        var output = writer.ToString();
        StringAssert.Contains(output, "\u2026");
        Assert.IsFalse(output.Contains(longSelector), "Full long selector should not appear verbatim");
    }

    [TestMethod]
    public void Print_Summary_ShowsCountsPerStatus()
    {
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(new[]
        {
            Make(SelectorCheckStatus.Healthy, matches: 1),
            Make(SelectorCheckStatus.Broken),
            Make(SelectorCheckStatus.Ambiguous, matches: 2),
            Make(SelectorCheckStatus.Skipped),
        }, writer, useColor: false);

        var output = writer.ToString();
        StringAssert.Contains(output, "Total 4");
        StringAssert.Contains(output, "1 healthy");
        StringAssert.Contains(output, "1 broken");
        StringAssert.Contains(output, "1 ambiguous");
        StringAssert.Contains(output, "1 skipped");
    }

    [TestMethod]
    public void Print_RankedSuggestions_EmitsOneLinePerSuggestionWithConfidenceAndStrategy()
    {
        var r = Make(SelectorCheckStatus.Broken, suggestion: "GetByTestId(\"a\")") with
        {
            Suggestions = new List<RepairSuggestion>
            {
                new("GetByTestId(\"a\")", "data-testid", Confidence.High),
                new("GetByRole(\"button\")", "role", Confidence.High),
            },
        };
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(new[] { r }, writer, useColor: false);

        var output = writer.ToString();
        StringAssert.Contains(output, "-> Suggestion (High, data-testid): GetByTestId(\"a\")");
        StringAssert.Contains(output, "-> Suggestion (High, role): GetByRole(\"button\")");
    }

    [TestMethod]
    public void Print_FixedRow_ShowsFixedStatusAndAppliedLine()
    {
        var r = Make(SelectorCheckStatus.Broken) with
        {
            Fixed = true,
            AppliedSuggestion = "GetByTestId(\"new\")",
        };
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(new[] { r }, writer, useColor: false);

        var output = writer.ToString();
        StringAssert.Contains(output, "FIXED");
        StringAssert.Contains(output, "-> Fixed: GetByTestId(\"new\")");
    }

    [TestMethod]
    public void Print_RewriteReport_EmitsFixedSummaryLine()
    {
        var broken = Make(SelectorCheckStatus.Broken);
        var fixed_ = Make(SelectorCheckStatus.Broken) with { Fixed = true, AppliedSuggestion = "x" };
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(
            new[] { broken, fixed_ },
            writer,
            useColor: false,
            rewriteReport: new RewriteReport(FilesModified: 1, FixesApplied: 1, FixesAttempted: 1));

        var output = writer.ToString();
        StringAssert.Contains(output, "Fixed 1 of 2 broken selectors in 1 files");
    }

    [TestMethod]
    public void Print_FixError_EmitsFixErrorLine()
    {
        var r = Make(SelectorCheckStatus.Broken) with { FixError = "source invocation not found" };
        var writer = new StringWriter();
        SelectorCheckTablePrinter.Print(new[] { r }, writer, useColor: false);
        StringAssert.Contains(writer.ToString(), "-> Fix error: source invocation not found");
    }
}
