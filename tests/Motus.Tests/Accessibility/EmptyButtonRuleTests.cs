using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class EmptyButtonRuleTests
{
    private readonly EmptyButtonRule _rule = new();
    private readonly AccessibilityAuditContext _context = new(AllNodes: [], Page: null!);

    private static AccessibilityNode BuildNode(
        string role,
        string? name = null,
        Dictionary<string, string?>? props = null) =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: props ?? new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: 1);

    [TestMethod]
    public void Evaluate_ButtonWithName_ReturnsNull()
    {
        var node = BuildNode("button", name: "Submit");
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_ButtonWithoutName_ReturnsViolation()
    {
        var node = BuildNode("button");
        var result = _rule.Evaluate(node, _context);

        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-empty-button", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
    }

    [TestMethod]
    public void Evaluate_HiddenButton_ReturnsNull()
    {
        var props = new Dictionary<string, string?> { ["hidden"] = "true" };
        var node = BuildNode("button", props: props);
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_NonButtonRole_ReturnsNull()
    {
        var node = BuildNode("link");
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_WhitespaceOnlyName_ReturnsViolation()
    {
        var node = BuildNode("button", name: "  ");
        Assert.IsNotNull(_rule.Evaluate(node, _context));
    }
}
