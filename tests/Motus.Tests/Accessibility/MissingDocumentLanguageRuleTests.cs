using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class MissingDocumentLanguageRuleTests
{
    private readonly MissingDocumentLanguageRule _rule = new();

    private static AccessibilityNode BuildNode(string role = "RootWebArea") =>
        new(
            NodeId: Guid.NewGuid().ToString(),
            Role: role,
            Name: null,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: 1);

    [TestMethod]
    public void Evaluate_WithDocumentLanguage_ReturnsNull()
    {
        var node = BuildNode();
        var nodes = new List<AccessibilityNode> { node };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes, Page: null!, DocumentLanguage: "en");

        Assert.IsNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_WithoutDocumentLanguage_ReturnsViolation()
    {
        var node = BuildNode();
        var nodes = new List<AccessibilityNode> { node };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes, Page: null!, DocumentLanguage: null);

        var result = _rule.Evaluate(node, context);
        Assert.IsNotNull(result);
        Assert.AreEqual("a11y-missing-lang", result.RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Severity);
    }

    [TestMethod]
    public void Evaluate_EmptyLanguage_ReturnsViolation()
    {
        var node = BuildNode();
        var nodes = new List<AccessibilityNode> { node };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes, Page: null!, DocumentLanguage: "");

        Assert.IsNotNull(_rule.Evaluate(node, context));
    }

    [TestMethod]
    public void Evaluate_OnlyFiresOnFirstNode()
    {
        var first = BuildNode();
        var second = BuildNode("heading");
        var nodes = new List<AccessibilityNode> { first, second };
        var context = new AccessibilityAuditContext(
            AllNodes: nodes, Page: null!, DocumentLanguage: null);

        Assert.IsNull(_rule.Evaluate(second, context));
    }
}
