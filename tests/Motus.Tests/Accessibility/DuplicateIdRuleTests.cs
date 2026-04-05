using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class DuplicateIdRuleTests
{
    private readonly DuplicateIdRule _rule = new();

    private static AccessibilityNode BuildNode() =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: "heading",
            Name: "Title",
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: 1);

    [TestMethod]
    public void Evaluate_NoDuplicates_ReturnsNull()
    {
        var node = BuildNode();
        var nodes = new List<AccessibilityNode> { node };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            DuplicateIds: new HashSet<string>());

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_WithDuplicates_ReturnsViolation()
    {
        var node = BuildNode();
        var nodes = new List<AccessibilityNode> { node };
        var duplicateIds = new HashSet<string> { "header", "content" };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            DuplicateIds: duplicateIds);

        var result = _rule.Evaluate(node, context);
        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-duplicate-id", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
        Assert.IsTrue(result.Message.Contains("header"));
        Assert.IsTrue(result.Message.Contains("content"));
    }

    [TestMethod]
    public void Evaluate_NullDuplicateIds_ReturnsNull()
    {
        var node = BuildNode();
        var nodes = new List<AccessibilityNode> { node };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            DuplicateIds: null);

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_OnlyFiresOnFirstNode()
    {
        var first = BuildNode();
        var second = BuildNode();
        var nodes = new List<AccessibilityNode> { first, second };
        var duplicateIds = new HashSet<string> { "header" };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            DuplicateIds: duplicateIds);

        Assert.IsNull(_rule.Evaluate(second, context));
    }
}
