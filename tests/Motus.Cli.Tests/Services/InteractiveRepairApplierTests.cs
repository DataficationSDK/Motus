using Motus.Cli.Services;
using Motus.Runner.Services.SelectorRepair;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class InteractiveRepairApplierTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Init()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "motus-interactive-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [TestMethod]
    public void Apply_RewritesSourceFile_AndReportsFixed()
    {
        var source =
            "class T {\n" +
            "    void M(dynamic page) {\n" +
            "        var x = page.GetByTestId(\"old-id\");\n" +
            "    }\n" +
            "}\n";
        var file = Path.Combine(_tempDir, "T.cs");
        File.WriteAllText(file, source);

        var item = new RepairQueueItem(
            Selector: "old-id",
            LocatorMethod: "GetByTestId",
            SourceFile: file,
            SourceLine: 3,
            PageUrl: "https://example.com",
            Suggestions: [new RepairCandidate("GetByTestId(\"new-id\")", "data-testid", RepairConfidence.High)],
            HighlightSelector: null);

        var outcome = InteractiveRepairApplier.Apply(item, "GetByTestId(\"new-id\")", backup: false, CancellationToken.None);

        Assert.IsTrue(outcome.Fixed, outcome.Error);
        Assert.IsNull(outcome.Error);

        var rewritten = File.ReadAllText(file);
        StringAssert.Contains(rewritten, "GetByTestId(\"new-id\")");
        Assert.IsFalse(rewritten.Contains("old-id"));
    }

    [TestMethod]
    public void Apply_AcceptsEditedReplacementChangingMethod()
    {
        var source =
            "class T {\n" +
            "    void M(dynamic page) {\n" +
            "        var x = page.Locator(\"#old\");\n" +
            "    }\n" +
            "}\n";
        var file = Path.Combine(_tempDir, "T.cs");
        File.WriteAllText(file, source);

        var item = new RepairQueueItem(
            Selector: "#old",
            LocatorMethod: "Locator",
            SourceFile: file,
            SourceLine: 3,
            PageUrl: "https://example.com",
            Suggestions: [new RepairCandidate("GetByRole(\"button\")", "role", RepairConfidence.Medium)],
            HighlightSelector: null);

        var outcome = InteractiveRepairApplier.Apply(item, "GetByTestId(\"hand-edited\")", backup: false, CancellationToken.None);

        Assert.IsTrue(outcome.Fixed, outcome.Error);
        var rewritten = File.ReadAllText(file);
        StringAssert.Contains(rewritten, "GetByTestId(\"hand-edited\")");
    }

    [TestMethod]
    public void Apply_InvalidReplacement_ReturnsErrorOutcome()
    {
        var source =
            "class T {\n" +
            "    void M(dynamic page) {\n" +
            "        var x = page.GetByTestId(\"old-id\");\n" +
            "    }\n" +
            "}\n";
        var file = Path.Combine(_tempDir, "T.cs");
        File.WriteAllText(file, source);

        var item = new RepairQueueItem(
            Selector: "old-id",
            LocatorMethod: "GetByTestId",
            SourceFile: file,
            SourceLine: 3,
            PageUrl: "https://example.com",
            Suggestions: [],
            HighlightSelector: null);

        // A replacement that does not parse as a member-access invocation.
        var outcome = InteractiveRepairApplier.Apply(item, "Get By Testid(\"x\")", backup: false, CancellationToken.None);

        Assert.IsFalse(outcome.Fixed);
        Assert.IsNotNull(outcome.Error);
    }
}
