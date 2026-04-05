using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that the page has at least one main landmark.
/// This is a page-level rule that evaluates against the full node list.
/// </summary>
internal sealed class MissingLandmarkRule : IAccessibilityRule
{
    public string RuleId => "a11y-missing-landmark";

    public string Description =>
        "Pages should have at least one main landmark for screen reader navigation.";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        // Only evaluate once: on the first node in the tree
        if (context.AllNodes.Count == 0 || !ReferenceEquals(node, context.AllNodes[0]))
            return null;

        foreach (var n in context.AllNodes)
        {
            if (string.Equals(n.Role, "main", StringComparison.OrdinalIgnoreCase))
                return null;
        }

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Warning,
            Message: "Page has no main landmark. Add a <main> element or role=\"main\" for screen reader navigation.",
            NodeRole: null,
            NodeName: null,
            BackendDOMNodeId: null,
            Selector: null);
    }
}
