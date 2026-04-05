using Motus.Abstractions;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilityRuleEngineTests
{
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

    private static AccessibilityAuditContext BuildContext(IReadOnlyList<AccessibilityNode> nodes) =>
        new(AllNodes: nodes, Page: null!);

    [TestMethod]
    public void Run_EmptyTree_ReturnsZeroViolations()
    {
        var engine = new AccessibilityRuleEngine([new AlwaysFailRule()]);
        var result = engine.Run([], BuildContext([]), null);

        Assert.AreEqual(0, result.ViolationCount);
        Assert.AreEqual(0, result.PassCount);
    }

    [TestMethod]
    public void Run_EmptyRules_ReturnsZeroViolations()
    {
        var nodes = new[] { BuildNode("button", "Submit") };
        var engine = new AccessibilityRuleEngine([]);
        var result = engine.Run(nodes, BuildContext(nodes), null);

        Assert.AreEqual(0, result.ViolationCount);
        Assert.AreEqual(0, result.PassCount);
    }

    [TestMethod]
    public void Run_SingleNode_RulePass_CountsAsPass()
    {
        var nodes = new[] { BuildNode("button", "Submit") };
        var engine = new AccessibilityRuleEngine([new AlwaysPassRule()]);
        var result = engine.Run(nodes, BuildContext(nodes), null);

        Assert.AreEqual(0, result.ViolationCount);
        Assert.AreEqual(1, result.PassCount);
    }

    [TestMethod]
    public void Run_SingleNode_RuleViolation_RecordsViolation()
    {
        var nodes = new[] { BuildNode("img", backendNodeId: 42) };
        var engine = new AccessibilityRuleEngine([new AlwaysFailRule()]);
        var result = engine.Run(nodes, BuildContext(nodes), null);

        Assert.AreEqual(1, result.ViolationCount);
        Assert.AreEqual("test-fail", result.Violations[0].RuleId);
        Assert.AreEqual(AccessibilityViolationSeverity.Error, result.Violations[0].Severity);
    }

    [TestMethod]
    public void Run_Deduplication_SameNodeSameRule_OnlyOneViolation()
    {
        var node = BuildNode("img", backendNodeId: 42);
        // Same node appears twice in the flat list (can happen with ARIA-owned trees)
        var nodes = new[] { node, node };
        var engine = new AccessibilityRuleEngine([new AlwaysFailRule()]);
        var result = engine.Run(nodes, BuildContext(nodes), null);

        Assert.AreEqual(1, result.ViolationCount);
    }

    [TestMethod]
    public void Run_MultipleRules_AllInvoked()
    {
        var nodes = new[] { BuildNode("button", "OK", backendNodeId: 1) };
        var pass = new AlwaysPassRule();
        var fail = new AlwaysFailRule();
        var engine = new AccessibilityRuleEngine([pass, fail]);
        var result = engine.Run(nodes, BuildContext(nodes), null);

        Assert.AreEqual(1, result.PassCount);
        Assert.AreEqual(1, result.ViolationCount);
    }

    [TestMethod]
    public void Run_WithDiagnosticMessage_PropagatesMessage()
    {
        var nodes = new[] { BuildNode("button", "OK") };
        var engine = new AccessibilityRuleEngine([new AlwaysPassRule()]);
        var result = engine.Run(nodes, BuildContext(nodes), "BiDi not supported");

        Assert.AreEqual("BiDi not supported", result.DiagnosticMessage);
    }

    [TestMethod]
    public void Run_Duration_IsNonNegative()
    {
        var nodes = new[] { BuildNode("button", "OK") };
        var engine = new AccessibilityRuleEngine([new AlwaysPassRule()]);
        var result = engine.Run(nodes, BuildContext(nodes), null);

        Assert.IsTrue(result.Duration >= TimeSpan.Zero);
    }

    // --- Test rule implementations ---

    private sealed class AlwaysPassRule : IAccessibilityRule
    {
        public string RuleId => "test-pass";
        public string Description => "Always passes.";

        public AccessibilityViolation? Evaluate(AccessibilityNode node, AccessibilityAuditContext context) =>
            null;
    }

    private sealed class AlwaysFailRule : IAccessibilityRule
    {
        public string RuleId => "test-fail";
        public string Description => "Always fails.";

        public AccessibilityViolation? Evaluate(AccessibilityNode node, AccessibilityAuditContext context) =>
            new(
                RuleId: RuleId,
                Severity: AccessibilityViolationSeverity.Error,
                Message: "Test failure.",
                NodeRole: node.Role,
                NodeName: node.Name,
                BackendDOMNodeId: node.BackendDOMNodeId,
                Selector: null);
    }
}
