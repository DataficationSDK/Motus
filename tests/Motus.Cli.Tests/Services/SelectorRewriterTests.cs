using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class SelectorRewriterTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "motus-rewriter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private string WriteFile(string content, string name = "Tests.cs")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static SelectorCheckResult BrokenResult(
        string sourceFile, int line, string selector, string method, string replacement,
        Confidence confidence = Confidence.High)
        => new(
            SelectorCheckStatus.Broken,
            Selector: selector,
            LocatorMethod: method,
            SourceFile: sourceFile,
            SourceLine: line,
            PageUrl: "https://example.com",
            MatchCount: 0,
            Suggestion: replacement,
            Note: null)
        {
            Suggestions = new List<RepairSuggestion>
            {
                new(replacement, StrategyName: "data-testid", Confidence: confidence),
            },
        };

    [TestMethod]
    public void Apply_ReplacesStringLiteral_AndPreservesSurroundingFormatting()
    {
        var source =
            "using System;\n" +
            "class T {\n" +
            "    void M(dynamic page) {\n" +
            "        var x = page.GetByTestId(\"old-id\");   // trailing comment\n" +
            "    }\n" +
            "}\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 4, selector: "old-id", method: "GetByTestId",
                replacement: "GetByTestId(\"new-id\")"),
        };

        var report = SelectorRewriter.Apply(results, backup: true, CancellationToken.None);

        Assert.AreEqual(1, report.FilesModified);
        Assert.AreEqual(1, report.FixesApplied);
        Assert.IsTrue(results[0].Fixed);
        Assert.AreEqual("GetByTestId(\"new-id\")", results[0].AppliedSuggestion);

        var rewritten = File.ReadAllText(file);
        StringAssert.Contains(rewritten, "GetByTestId(\"new-id\")");
        StringAssert.Contains(rewritten, "// trailing comment",
            "Trailing trivia must be preserved");
        Assert.IsFalse(rewritten.Contains("old-id"));
        Assert.IsTrue(File.Exists(file + ".bak"));
    }

    [TestMethod]
    public void Apply_WithNoBackup_DoesNotWriteBackupFile()
    {
        var source = "class T { void M(dynamic page) { page.GetByTestId(\"old\"); } }\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 1, selector: "old", method: "GetByTestId",
                replacement: "GetByTestId(\"new\")"),
        };

        SelectorRewriter.Apply(results, backup: false, CancellationToken.None);

        Assert.IsFalse(File.Exists(file + ".bak"));
    }

    [TestMethod]
    public void Apply_SkipsMediumAndLowConfidence()
    {
        var source = "class T { void M(dynamic page) { page.GetByTestId(\"old\"); } }\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 1, selector: "old", method: "GetByTestId",
                replacement: "GetByTestId(\"new\")", confidence: Confidence.Medium),
        };

        var report = SelectorRewriter.Apply(results, backup: true, CancellationToken.None);

        Assert.AreEqual(0, report.FixesApplied);
        Assert.IsFalse(results[0].Fixed);
        StringAssert.Contains(File.ReadAllText(file), "\"old\"");
    }

    [TestMethod]
    public void Apply_MultipleRewritesInSameFile_BothApplied()
    {
        var source =
            "class T {\n" +
            "    void A(dynamic page) { page.GetByTestId(\"a\"); }\n" +
            "    void B(dynamic page) { page.GetByTestId(\"b\"); }\n" +
            "}\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 2, selector: "a", method: "GetByTestId",
                replacement: "GetByTestId(\"a2\")"),
            BrokenResult(file, line: 3, selector: "b", method: "GetByTestId",
                replacement: "GetByTestId(\"b2\")"),
        };

        var report = SelectorRewriter.Apply(results, backup: false, CancellationToken.None);

        Assert.AreEqual(2, report.FixesApplied);
        Assert.AreEqual(1, report.FilesModified);
        var rewritten = File.ReadAllText(file);
        StringAssert.Contains(rewritten, "GetByTestId(\"a2\")");
        StringAssert.Contains(rewritten, "GetByTestId(\"b2\")");
    }

    [TestMethod]
    public void Apply_ChangesLocatorMethod_WhenSuggestionUsesDifferentFactory()
    {
        var source = "class T { void M(dynamic page) { page.Locator(\"#old\"); } }\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 1, selector: "#old", method: "Locator",
                replacement: "GetByTestId(\"new\")"),
        };

        SelectorRewriter.Apply(results, backup: false, CancellationToken.None);

        var rewritten = File.ReadAllText(file);
        StringAssert.Contains(rewritten, "page.GetByTestId(\"new\")");
        Assert.IsFalse(rewritten.Contains("Locator"));
    }

    [TestMethod]
    public void Apply_WrongLineNumber_RecordsFixError()
    {
        var source = "class T { void M(dynamic page) { page.GetByTestId(\"old\"); } }\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 99, selector: "old", method: "GetByTestId",
                replacement: "GetByTestId(\"new\")"),
        };

        var report = SelectorRewriter.Apply(results, backup: false, CancellationToken.None);

        Assert.AreEqual(0, report.FixesApplied);
        Assert.IsFalse(results[0].Fixed);
        Assert.IsNotNull(results[0].FixError);
        StringAssert.Contains(results[0].FixError!, "not found");
        // Source file was not touched.
        StringAssert.Contains(File.ReadAllText(file), "\"old\"");
    }

    [TestMethod]
    public void Apply_UnparseableSuggestion_RecordsFixError()
    {
        var source = "class T { void M(dynamic page) { page.GetByTestId(\"old\"); } }\n";
        var file = WriteFile(source);

        var results = new List<SelectorCheckResult>
        {
            BrokenResult(file, line: 1, selector: "old", method: "GetByTestId",
                replacement: "not a valid call expression ))"),
        };

        SelectorRewriter.Apply(results, backup: false, CancellationToken.None);

        Assert.IsFalse(results[0].Fixed);
        Assert.IsNotNull(results[0].FixError);
    }

    [TestMethod]
    public void Apply_NoEligibleResults_ReturnsZero()
    {
        var results = new List<SelectorCheckResult>
        {
            new(SelectorCheckStatus.Healthy, "#ok", "Locator", "/x.cs", 1, "u", 1, null, null),
        };
        var report = SelectorRewriter.Apply(results, backup: false, CancellationToken.None);
        Assert.AreEqual(0, report.FilesModified);
        Assert.AreEqual(0, report.FixesApplied);
    }
}
