using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that link elements have a non-empty accessible name.
/// </summary>
internal sealed class EmptyLinkRule : IAccessibilityRule
{
    public string RuleId => "a11y-empty-link";

    public string Description =>
        "Links must have a non-empty accessible name.";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (!string.Equals(node.Role, "link", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.IsNullOrWhiteSpace(node.Name))
            return null;

        if (node.Properties.TryGetValue("hidden", out var hidden) &&
            string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: "Link has no accessible name. Add text content, aria-label, or aria-labelledby.",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
