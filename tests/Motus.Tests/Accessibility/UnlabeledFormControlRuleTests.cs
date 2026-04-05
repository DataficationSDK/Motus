using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class UnlabeledFormControlRuleTests
{
    private readonly UnlabeledFormControlRule _rule = new();
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
    [DataRow("textbox")]
    [DataRow("combobox")]
    [DataRow("listbox")]
    [DataRow("checkbox")]
    [DataRow("radio")]
    [DataRow("slider")]
    [DataRow("spinbutton")]
    [DataRow("switch")]
    public void Evaluate_UnlabeledControl_ReturnsViolation(string role)
    {
        var node = BuildNode(role);
        var result = _rule.Evaluate(node, _context);

        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-unlabeled-form-control", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
    }

    [TestMethod]
    [DataRow("textbox")]
    [DataRow("checkbox")]
    public void Evaluate_LabeledControl_ReturnsNull(string role)
    {
        var node = BuildNode(role, name: "Email address");
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_NonFormControlRole_ReturnsNull()
    {
        var node = BuildNode("button");
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_HiddenControl_ReturnsNull()
    {
        var props = new Dictionary<string, string?> { ["hidden"] = "true" };
        var node = BuildNode("textbox", props: props);
        Assert.IsNull(_rule.Evaluate(node, _context));
    }

    [TestMethod]
    public void Evaluate_WhitespaceOnlyName_ReturnsViolation()
    {
        var node = BuildNode("textbox", name: "   ");
        Assert.IsNotNull(_rule.Evaluate(node, _context));
    }
}
