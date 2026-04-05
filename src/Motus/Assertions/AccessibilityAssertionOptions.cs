namespace Motus.Assertions;

/// <summary>
/// Configuration for the <c>ToPassAccessibilityAuditAsync</c> assertion.
/// </summary>
public sealed class AccessibilityAssertionOptions
{
    private readonly List<string> _skip = [];

    /// <summary>
    /// Excludes the specified rule IDs from the pass/fail evaluation.
    /// </summary>
    public AccessibilityAssertionOptions SkipRules(params string[] ruleIds)
    {
        _skip.AddRange(ruleIds);
        return this;
    }

    /// <summary>Whether warnings count as failures alongside errors. Default: true.</summary>
    public bool IncludeWarnings { get; set; } = true;

    internal IReadOnlyList<string> SkippedRules => _skip;
}
