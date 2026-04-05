using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AltTextRuleTests
{
    private readonly AltTextAccessibilityRule _rule = new();
    private readonly AccessibilityAuditContext _context = new(AllNodes: [], Page: null!);

    private static AccessibilityNode BuildNode(
        string role,
        string? name = null,
        long? backendNodeId = null,
        Dictionary<string, string?>? props = null) =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: props ?? new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: backendNodeId);

    [TestMethod]
    public void Evaluate_ImgWithAltText_ReturnsNull()
    {
        var node = BuildNode("img", name: "Company logo");
        var result = _rule.Evaluate(node, _context);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Evaluate_ImgWithoutAltText_ReturnsViolation()
    {
        var node = BuildNode("img", backendNodeId: 10);
        var result = _rule.Evaluate(node, _context);

        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-alt-text", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
        Assert.AreEqual(10L, result.BackendDOMNodeId);
    }

    [TestMethod]
    public void Evaluate_ImgWithEmptyName_ReturnsViolation()
    {
        var node = BuildNode("img", name: "   ");
        var result = _rule.Evaluate(node, _context);
        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void Evaluate_NonImgRole_ReturnsNull()
    {
        var node = BuildNode("button", name: null);
        var result = _rule.Evaluate(node, _context);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Evaluate_ImgHidden_ReturnsNull()
    {
        var props = new Dictionary<string, string?> { ["hidden"] = "true" };
        var node = BuildNode("img", name: null, props: props);
        var result = _rule.Evaluate(node, _context);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void Evaluate_ImgNotHidden_ReturnsViolation()
    {
        var props = new Dictionary<string, string?> { ["hidden"] = "false" };
        var node = BuildNode("img", name: null, props: props);
        var result = _rule.Evaluate(node, _context);
        Assert.IsNotNull(result);
    }
}
