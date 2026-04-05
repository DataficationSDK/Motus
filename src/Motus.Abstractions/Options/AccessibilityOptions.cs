namespace Motus.Abstractions;

/// <summary>
/// Configuration for the built-in accessibility audit hook.
/// </summary>
public sealed record AccessibilityOptions
{
    /// <summary>Whether the accessibility audit hook is enabled. Default: false.</summary>
    public bool Enable { get; init; }

    /// <summary>How violations are handled. Default: Enforce.</summary>
    public AccessibilityMode Mode { get; init; } = AccessibilityMode.Enforce;

    /// <summary>Whether to run an audit after each navigation. Default: true.</summary>
    public bool AuditAfterNavigation { get; init; } = true;

    /// <summary>
    /// Whether to run an audit after mutating actions (click, fill, selectOption).
    /// Default: false.
    /// </summary>
    public bool AuditAfterActions { get; init; }

    /// <summary>Whether warnings count as failures alongside errors. Default: true.</summary>
    public bool IncludeWarnings { get; init; } = true;

    /// <summary>Rule IDs to exclude from audits (e.g., "a11y-color-contrast").</summary>
    public IReadOnlyList<string>? SkipRules { get; init; }
}
