using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class EmptyLinkRuleTests
{
    private readonly EmptyLinkRule _rule = new();
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
    public void Evaluate_LinkWithName_ReturnsNull()
    {
        var node = BuildNode("link", name: "Home");
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_LinkWithoutName_ReturnsViolation()
    {
        var node = BuildNode("link");
        var result = _rule.Evaluate(node, _context);

        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-empty-link", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
    }

    [TestMethod]
    public void Evaluate_HiddenLink_ReturnsNull()
    {
        var props = new Dictionary<string, string?> { ["hidden"] = "true" };
        var node = BuildNode("link", props: props);
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_NonLinkRole_ReturnsNull()
    {
        var node = BuildNode("button");
        Assert.IsNull(_rule.Evaluate(node, _context));
    }
}
