using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Checks that text elements meet WCAG AA color contrast requirements.
/// Uses pre-fetched computed styles from the audit context.
/// </summary>
internal sealed class ColorContrastRule : IAccessibilityRule
{
    private static readonly HashSet<string> NonTextRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "separator", "none", "presentation"
    };

    public string RuleId => "a11y-color-contrast";

    public string Description =>
        "Text elements must have sufficient color contrast (4.5:1 for normal text, 3:1 for large text).";

    public AccessibilityViolation? Evaluate(
        AccessibilityNode node,
        AccessibilityAuditContext context)
    {
        if (context.ComputedStyles is null)
            return null;

        if (node.BackendDOMNodeId is null)
            return null;

        if (string.IsNullOrWhiteSpace(node.Name))
            return null;

        if (node.Role is not null && NonTextRoles.Contains(node.Role))
            return null;

        if (node.Properties.TryGetValue("hidden", out var hidden) &&
            string.Equals(hidden, "true", StringComparison.OrdinalIgnoreCase))
            return null;

        if (!context.ComputedStyles.TryGetValue(node.BackendDOMNodeId.Value, out var style))
            return null;

        if (!ContrastCalculator.TryParseColor(style.Color, out var fgR, out var fgG, out var fgB))
            return null;

        if (!ContrastCalculator.TryParseColor(style.BackgroundColor, out var bgR, out var bgG, out var bgB))
            return null;

        var fgLuminance = ContrastCalculator.RelativeLuminance(fgR, fgG, fgB);
        var bgLuminance = ContrastCalculator.RelativeLuminance(bgR, bgG, bgB);
        var ratio = ContrastCalculator.ContrastRatio(fgLuminance, bgLuminance);

        var isLarge = ContrastCalculator.IsLargeText(style.FontSize, style.FontWeight);
        var threshold = isLarge
            ? ContrastCalculator.LargeTextThreshold
            : ContrastCalculator.NormalTextThreshold;

        if (ratio >= threshold)
            return null;

        return new AccessibilityViolation(
            RuleId: RuleId,
            Severity: AccessibilityViolationSeverity.Error,
            Message: $"Insufficient color contrast: {ratio:F2}:1 (required {threshold:F1}:1 for " +
                     $"{(isLarge ? "large" : "normal")} text). " +
                     $"Foreground: {style.Color}, Background: {style.BackgroundColor}.",
            NodeRole: node.Role,
            NodeName: node.Name,
            BackendDOMNodeId: node.BackendDOMNodeId,
            Selector: null);
    }
}
