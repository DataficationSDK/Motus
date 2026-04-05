namespace Motus.Abstractions;

/// <summary>
/// Evaluates a single accessibility rule against one node in the accessibility tree.
/// </summary>
public interface IAccessibilityRule
{
    /// <summary>
    /// Gets the unique identifier for this rule (e.g. "a11y-alt-text").
    /// </summary>
    string RuleId { get; }

    /// <summary>
    /// Gets a human-readable description of what this rule checks.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Evaluates the rule against the given node.
    /// </summary>
    /// <param name="node">The node to evaluate.</param>
    /// <param name="context">The audit context providing tree-wide data and page access.</param>
    /// <returns>
    /// A <see cref="AccessibilityViolation"/> if this node violates the rule;
    /// <c>null</c> if the node passes or this rule does not apply to the node.
    /// </returns>
    AccessibilityViolation? Evaluate(AccessibilityNode node, AccessibilityAuditContext context);
}
