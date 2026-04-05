using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class MissingLandmarkRuleTests
{
    private readonly MissingLandmarkRule _rule = new();

    private static AccessibilityNode BuildNode(string role, string? name = null) =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: 1);

    [TestMethod]
    public void Evaluate_PageWithMainLandmark_ReturnsNull()
    {
        var nodes = new List<AccessibilityNode>
        {
            BuildNode("navigation", "Nav"),
            BuildNode("main", "Content"),
            BuildNode("heading", "Title")
        };

        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);
        Assert.IsNull(_rule.Evaluate(nodes[0], context));
    }

    [TestMethod]
    public void Evaluate_PageWithoutMainLandmark_ReturnsViolation()
    {
        var nodes = new List<AccessibilityNode>
        {
            BuildNode("navigation", "Nav"),
            BuildNode("heading", "Title")
        };

        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);
        var result = _rule.Evaluate(nodes[0], context);

        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-missing-landmark", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Warning, result.Severity);
    }

    [TestMethod]
    public void Evaluate_OnlyFiresOnFirstNode()
    {
        var nodes = new List<AccessibilityNode>
        {
            BuildNode("navigation", "Nav"),
            BuildNode("heading", "Title")
        };

        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);

        // Should not fire on the second node
        Assert.IsNull(_rule.Evaluate(nodes[1], context));
    }

    [TestMethod]
    public void Evaluate_EmptyTree_ReturnsNull()
    {
        var nodes = new List<AccessibilityNode>();
        var context = new AccessibilityAuditContext(AllNodes: nodes, Page: null!);
        var node = BuildNode("navigation");
        Assert.IsNull(_rule.Evaluate(node, context));
    }
}
