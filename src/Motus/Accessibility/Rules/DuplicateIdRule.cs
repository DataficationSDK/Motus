using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks for duplicate id attributes in the document.
/// Uses pre-fetched duplicate ID set from the audit context.
/// </summary>
internal sealed class DuplicateIdRule : IAccessibilityRule
{
    public string RuleId => "a11y-duplicate-id";

    public string Description =>
        "Each id attribute value must be unique within the document.";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (context.DuplicateIds is null || context.DuplicateIds.Count == 0)
            return null;

        // Only evaluate once: on the first node in the tree
        if (context.AllNodes.Count == 0 || !ReferenceEquals(node, context.AllNodes[0]))
            return null;

        // Report one violation per duplicate id
        foreach (var id in context.DuplicateIds)
        {
            // Return the first one; the rule engine deduplicates by (RuleId, dedupeKey).
            // Since this is a page-level check, we report each duplicate id as a separate violation
            // but since Evaluate returns a single violation, we return just the first.
            // The rule engine calls us once per node, and we only fire on the first node,
            // so we need to return a single representative violation.
            return new AccessibilityViolation(
                RuleId: RuleId,
                Severity: AccessibilityViolationSeverity.Error,
                Message: $"Duplicate id attribute values found: {string.Join(", ", context.DuplicateIds.Select(i => $"\"{i}\""))}. " +
                         "Each id must be unique.",
                NodeRole: null,
                NodeName: null,
                BackendDOMNodeId: null,
                Selector: null);
        }

        return null;
    }
}
