using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that img elements have a non-empty accessible name.
/// </summary>
internal sealed class AltTextAccessibilityRule : IAccessibilityRule
{
    public string RuleId => "a11y-alt-text";

    public string Description =>
        "Images must have a non-empty accessible name (alt text or aria-label).";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (!string.Equals(node.Role, "img", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!string.IsNullOrWhiteSpace(node.Name))
            return null;

        // Decorative images marked as hidden are intentionally unnamed
        if (node.Properties.TryGetValue("hidden", out var hidden) &&
            string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: "Image has no accessible name. Add alt text or aria-label.",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
