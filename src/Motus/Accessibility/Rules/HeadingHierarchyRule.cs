using System.Globalization;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that heading levels do not skip (e.g., h1 to h3 without h2).
/// </summary>
internal sealed class HeadingHierarchyRule : IAccessibilityRule
{
    public string RuleId => "a11y-heading-hierarchy";

    public string Description =>
        "Heading levels should not skip (e.g., h1 followed by h3 without an h2).";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (!string.Equals(node.Role, "heading", StringComparison.OrdinalIgnoreCase))
            return null;

        var level = GetLevel(node);
        if (level is null)
            return null;

        // Find the preceding heading in document order
        var previousLevel = FindPreviousHeadingLevel(node, context.AllNodes);
        if (previousLevel is null)
            return null;

        // A skip is when the current level is more than one deeper than the previous
        if (level.Value > previousLevel.Value + 1)
        {
            return new AccessibilityViolation(
                RuleId: RuleId,
                Severity: AccessibilityViolationSeverity.Warning,
                Message: $"Heading level {level.Value} skips from level {previousLevel.Value}. " +
                         $"Do not skip heading levels.",
                NodeRole: node.Role,
                NodeName: node.Name,
                BackendDOMNodeId: node.BackendDOMNodeId,
                Selector: null);
        }

        return null;
    }

    private static int? GetLevel(AccessibilityNode node)
    {
        if (node.Properties.TryGetValue("level", out var levelStr) &&
            int.TryParse(levelStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level))
            return level;

        return null;
    }

    private static int? FindPreviousHeadingLevel(
        AccessibilityNode target,
        IReadOnlyList<AccessibilityNode> allNodes)
    {
        int? previousLevel = null;

        foreach (var n in allNodes)
        {
            if (ReferenceEquals(n, target))
                break;

            if (string.Equals(n.Role, "heading", StringComparison.OrdinalIgnoreCase))
            {
                var level = GetLevel(n);
                if (level.HasValue)
                    previousLevel = level.Value;
            }
        }

        return previousLevel;
    }
}
