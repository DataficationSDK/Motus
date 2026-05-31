using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class AccessibilityToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // Two element nodes (backend ids 5 and 7) take refs e1 and e2 in document order;
    // a third violation has no backend node and so maps to no ref.
    private static AccessibilitySnapshot TwoElementSnapshot() => new(
        Roots:
        [
            new AccessibilityNode("1", "img", "", null, null, new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
            new AccessibilityNode("2", "button", "", null, null, new Dictionary<string, string?>(), [], BackendDOMNodeId: 7),
        ],
        IgnoredCount: 0,
        DiagnosticMessage: null);

    private static AccessibilityAuditResult ThreeViolations() => new(
        Violations:
        [
            new AccessibilityViolation("a11y-alt-text", AccessibilityViolationSeverity.Error,
                "Image has no alt text.", "img", "", BackendDOMNodeId: 5, Selector: null),
            new AccessibilityViolation("a11y-empty-button", AccessibilityViolationSeverity.Warning,
                "Button has no accessible name.", "button", "", BackendDOMNodeId: 7, Selector: null),
            new AccessibilityViolation("a11y-document-language", AccessibilityViolationSeverity.Error,
                "Document has no lang attribute.", null, null, BackendDOMNodeId: null, Selector: null),
        ],
        PassCount: 4,
        ViolationCount: 3,
        Duration: TimeSpan.Zero);

    private static IReadOnlyList<JsonElement> Violations(CallToolResult result)
    {
        Assert.IsNotNull(result.StructuredContent, "expected structured content");
        return result.StructuredContent!.Value.GetProperty("violations").EnumerateArray().ToArray();
    }

    private static JsonElement ByRule(CallToolResult result, string ruleId)
        => Violations(result).Single(v => v.GetProperty("ruleId").GetString() == ruleId);

    private static string? RefOf(JsonElement violation)
        => violation.GetProperty("ref").ValueKind == JsonValueKind.Null
            ? null
            : violation.GetProperty("ref").GetString();

    [TestMethod]
    public async Task Audit_MapsElementViolationsToRefs_AndLeavesPageLevelUnref()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditResult = ThreeViolations() };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(null, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(3, result.StructuredContent!.Value.GetProperty("violationCount").GetInt32());

        var altText = ByRule(result, "a11y-alt-text");
        Assert.AreEqual("Error", altText.GetProperty("severity").GetString());
        Assert.AreEqual("e1", RefOf(altText));

        Assert.AreEqual("e2", RefOf(ByRule(result, "a11y-empty-button")));

        // The page-level violation has no addressable element, so its ref is null.
        Assert.IsNull(RefOf(ByRule(result, "a11y-document-language")));
    }

    [TestMethod]
    public async Task Audit_MinSeverityError_ReturnsOnlyErrors()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditResult = ThreeViolations() };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync("error", service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        var rules = Violations(result).Select(v => v.GetProperty("ruleId").GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "a11y-alt-text", "a11y-document-language" }, rules);
    }

    [TestMethod]
    public async Task Audit_NoViolations_ReportsNoneWithoutError()
    {
        var page = new FakeToolPage(TwoElementSnapshot());
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(null, service, Ct);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsNull(result.StructuredContent);
        StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "No accessibility violations");
    }

    [TestMethod]
    public async Task Audit_UnknownSeverity_ReturnsError()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditResult = ThreeViolations() };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync("bogus", service, Ct);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "bogus");
    }

    [TestMethod]
    public async Task Audit_WhenAuditThrows_ReturnsError()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditError = new InvalidOperationException("boom") };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(null, service, Ct);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "boom");
    }
}
