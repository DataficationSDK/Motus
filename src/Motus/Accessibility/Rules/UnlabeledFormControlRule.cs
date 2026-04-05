using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that form control elements have a non-empty accessible name.
/// </summary>
internal sealed class UnlabeledFormControlRule : IAccessibilityRule
{
    private static readonly HashSet<string> FormControlRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox", "combobox", "listbox", "checkbox",
        "radio", "slider", "spinbutton", "switch"
    };

    public string RuleId => "a11y-unlabeled-form-control";

    public string Description =>
        "Form controls must have a non-empty accessible name (label, aria-label, or aria-labelledby).";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (node.Role is null || !FormControlRoles.Contains(node.Role))
            return null;

        if (!string.IsNullOrWhiteSpace(node.Name))
            return null;

        if (node.Properties.TryGetValue("hidden", out var hidden) &&
            string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: $"Form control (role: {node.Role}) has no accessible name. Add a label, aria-label, or aria-labelledby.",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
