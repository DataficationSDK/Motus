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

    // Two element nodes: an unnamed image (backend id 5), which the snapshot does not give a ref
    // because it is neither interactive nor named, and an unnamed button (backend id 7), which
    // takes e1 because a button is always worth targeting. A third violation has no backend
    // node at all.
    private static AccessibilitySnapshot TwoElementSnapshot() => new(
        Roots:
        [
            new AccessibilityNode("1", "img", "", null, null, new Dictionary<string, string?>(), [], BackendDOMNodeId: 5),
            new AccessibilityNode("2", "button", "", null, null, new Dictionary<string, string?>(),
                [
                    new AccessibilityNode("3", "StaticText", "Go", null, null, new Dictionary<string, string?>(), [], BackendDOMNodeId: 9),
                ],
                BackendDOMNodeId: 7),
        ],
        IgnoredCount: 0,
        DiagnosticMessage: null);

    private static AccessibilityAuditResult ThreeViolations() => new(
        Violations:
        [
            new AccessibilityViolation("a11y-alt-text", AccessibilityViolationSeverity.Error,
                "Image has no alt text.", "img", "", BackendDOMNodeId: 5, Selector: "img:nth-of-type(1)"),
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

    private static string? StringOf(JsonElement violation, string property)
        => violation.GetProperty(property).ValueKind == JsonValueKind.Null
            ? null
            : violation.GetProperty(property).GetString();

    [TestMethod]
    public async Task Audit_MapsElementViolationsToRefs_AndDescribesTheRest()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditResult = ThreeViolations() };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(
            pageService: service,
            cancellationToken: Ct,
            min_severity: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(3, result.StructuredContent!.Value.GetProperty("violationCount").GetInt32());

        // The snapshot gave the unnamed image no ref, and the audit does not print one just for
        // the violation's sake: the role, name, and selector are what identify it.
        var altText = ByRule(result, "a11y-alt-text");
        Assert.AreEqual("Error", altText.GetProperty("severity").GetString());
        Assert.IsNull(RefOf(altText));
        Assert.AreEqual("img", StringOf(altText, "nodeRole"));
        Assert.AreEqual("img:nth-of-type(1)", StringOf(altText, "selector"));
        Assert.IsNull(StringOf(altText, "nodeText"));

        // The button is interactive, so it has a ref, and its text says which button it is.
        var emptyButton = ByRule(result, "a11y-empty-button");
        Assert.AreEqual("e1", RefOf(emptyButton));
        Assert.AreEqual("Go", StringOf(emptyButton, "nodeText"));
        Assert.IsNull(StringOf(emptyButton, "selector"));

        // The page-level violation has no element at all, so its ref is null.
        Assert.IsNull(RefOf(ByRule(result, "a11y-document-language")));
    }

    [TestMethod]
    public async Task Audit_MinSeverityError_ReturnsOnlyErrors()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditResult = ThreeViolations() };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(
            pageService: service,
            cancellationToken: Ct,
            min_severity: "error");

        Assert.IsFalse(result.IsError ?? false);
        var rules = Violations(result).Select(v => v.GetProperty("ruleId").GetString()).ToArray();
        CollectionAssert.AreEquivalent(new[] { "a11y-alt-text", "a11y-document-language" }, rules);
    }

    [TestMethod]
    public async Task Audit_NoViolations_ReportsNoneWithoutError()
    {
        var page = new FakeToolPage(TwoElementSnapshot());
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(
            pageService: service,
            cancellationToken: Ct,
            min_severity: null);

        Assert.IsFalse(result.IsError ?? false);
        Assert.IsNull(result.StructuredContent);
        StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "No accessibility violations");
    }

    [TestMethod]
    public async Task Audit_UnknownSeverity_ReturnsError()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditResult = ThreeViolations() };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(
            pageService: service,
            cancellationToken: Ct,
            min_severity: "bogus");

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "bogus");
    }

    [TestMethod]
    public async Task Audit_WhenAuditThrows_ReturnsError()
    {
        var page = new FakeToolPage(TwoElementSnapshot()) { AuditError = new InvalidOperationException("boom") };
        var service = new FakeActivePageService(page);

        var result = await AccessibilityTools.AuditAccessibilityAsync(
            pageService: service,
            cancellationToken: Ct,
            min_severity: null);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(((TextContentBlock)result.Content[0]).Text, "boom");
    }
}
