using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class ColorContrastRuleTests
{
    private readonly ColorContrastRule _rule = new();

    private static AccessibilityNode BuildTextNode(
        string role = "heading",
        string? name = "Title",
        long? backendNodeId = 1) =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: backendNodeId);

    private static AccessibilityAuditContext BuildContext(
        AccessibilityNode node,
        ComputedStyleInfo? style)
    {
        var nodes = new List<AccessibilityNode> { node };
        var styles = new Dictionary<long, ComputedStyleInfo>();
        if (style is not null && node.BackendDOMNodeId.HasValue)
            styles[node.BackendDOMNodeId.Value] = style;

        return new AccessibilityAuditContext(
            AllNodes: nodes,
            Page: null!,
            ComputedStyles: styles);
    }

    [TestMethod]
    public void Evaluate_SufficientContrast_ReturnsNull()
    {
        var node = BuildTextNode();
        var style = new ComputedStyleInfo("rgb(0, 0, 0)", "rgb(255, 255, 255)", "16px", "400");
        var context = BuildContext(node, style);

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_InsufficientContrast_ReturnsViolation()
    {
        var node = BuildTextNode();
        // Light gray on white has low contrast
        var style = new ComputedStyleInfo("rgb(200, 200, 200)", "rgb(255, 255, 255)", "16px", "400");
        var context = BuildContext(node, style);

        var result = _rule.Evaluate(node, context);
        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-color-contrast", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
    }

    [TestMethod]
    public void Evaluate_LargeTextLowerThreshold()
    {
        var node = BuildTextNode();
        // This contrast ratio passes 3:1 (large text) but fails 4.5:1 (normal)
        // #949494 on white = ~3.03:1
        var style = new ComputedStyleInfo("rgb(148, 148, 148)", "rgb(255, 255, 255)", "24px", "400");
        var context = BuildContext(node, style);

        // Should pass for large text
        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_NoComputedStyles_ReturnsNull()
    {
        var node = BuildTextNode();
        var context = new AccessibilityAuditContext(
            AllNodes: new List<AccessibilityNode> { node },
            Page: null!,
            ComputedStyles: null);

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_NoBackendNodeId_ReturnsNull()
    {
        var node = BuildTextNode(backendNodeId: null);
        var context = new AccessibilityAuditContext(
            AllNodes: new List<AccessibilityNode> { node },
            Page: null!,
            ComputedStyles: new Dictionary<long, ComputedStyleInfo>());

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_ImgRole_ReturnsNull()
    {
        var node = BuildTextNode(role: "img", name: "Logo");
        var style = new ComputedStyleInfo("rgb(200, 200, 200)", "rgb(255, 255, 255)", "16px", "400");
        var context = BuildContext(node, style);

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_HiddenNode_ReturnsNull()
    {
        var node = new AccessibilityNode(
            NodeId: Guid.NewGuid().ToString(),
            Role: "heading",
            Name: "Title",
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?> { ["hidden"] = "true" },
            Children: [],
            BackendDOMNodeId: 1);

        var style = new ComputedStyleInfo("rgb(200, 200, 200)", "rgb(255, 255, 255)", "16px", "400");
        var context = BuildContext(node, style);

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_EmptyName_ReturnsNull()
    {
        var node = BuildTextNode(name: null);
        var style = new ComputedStyleInfo("rgb(200, 200, 200)", "rgb(255, 255, 255)", "16px", "400");
        var context = BuildContext(node, style);

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_UnparsableColor_ReturnsNull()
    {
        var node = BuildTextNode();
        var style = new ComputedStyleInfo("not-a-color", "rgb(255, 255, 255)", "16px", "400");
        var context = BuildContext(node, style);

        Assert.IsNull(_rule.Evaluate(node, context));
    }
}
