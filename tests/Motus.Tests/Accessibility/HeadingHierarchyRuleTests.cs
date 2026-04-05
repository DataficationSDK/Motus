using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class HeadingHierarchyRuleTests
{
    private readonly HeadingHierarchyRule _rule = new();

    private static AccessibilityNode BuildHeading(int level, string? name = null) =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: "heading",
            Name: name ?? $"Heading {level}",
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?> { ["level"] = level.ToString() },
            Children: [],
            BackendDOMNodeId: level);

    private static AccessibilityNode BuildNonHeading(string role = "paragraph") =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: role,
            Name: null,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: null);

    [TestMethod]
    public void Evaluate_SequentialHeadings_ReturnsNull()
    {
        var h1 = BuildHeading(1);
        var h2 = BuildHeading(2);
        var nodes = new List<AccessibilityNode> { h1, h2 };
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        Assert.IsNull(_rule.Evaluate(h1, context));
        Assert.IsNull(_rule.Evaluate(h2, context));
    }

    [TestMethod]
    public void Evaluate_SkippedLevel_ReturnsViolation()
    {
        var h1 = BuildHeading(1);
        var h3 = BuildHeading(3);
        var nodes = new List<AccessibilityNode> { h1, h3 };
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        var result = _rule.Evaluate(h3, context);
        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-heading-hierarchy", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Warning, result.Severity);
        Assert.IsTrue(result.Message.Contains("3"));
        Assert.IsTrue(result.Message.Contains("1"));
    }

    [TestMethod]
    public void Evaluate_FirstHeading_NoPrevious_ReturnsNull()
    {
        var h2 = BuildHeading(2);
        var nodes = new List<AccessibilityNode> { h2 };
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        Assert.IsNull(_rule.Evaluate(h2, context));
    }

    [TestMethod]
    public void Evaluate_DecreasingLevel_ReturnsNull()
    {
        var h3 = BuildHeading(3);
        var h1 = BuildHeading(1);
        var nodes = new List<AccessibilityNode> { h3, h1 };
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        Assert.IsNull(_rule.Evaluate(h1, context));
    }

    [TestMethod]
    public void Evaluate_NonHeading_ReturnsNull()
    {
        var p = BuildNonHeading();
        var nodes = new List<AccessibilityNode> { p };
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        Assert.IsNull(_rule.Evaluate(p, context));
    }

    [TestMethod]
    public void Evaluate_H1ToH4Skip_ReturnsViolation()
    {
        var h1 = BuildHeading(1);
        var h4 = BuildHeading(4);
        var nodes = new List<AccessibilityNode> { h1, h4 };
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        Assert.IsNotNull(_rule.Evaluate(h4, context));
    }
}
