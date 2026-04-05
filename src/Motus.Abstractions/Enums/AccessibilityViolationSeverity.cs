namespace Motus.Abstractions;

/// <summary>
/// Classifies the severity of an accessibility rule violation.
/// </summary>
public enum AccessibilityViolationSeverity
{
    /// <summary>A definite accessibility failure that prevents use.</summary>
    Error,

    /// <summary>A likely issue that may impair some users.</summary>
    Warning,

    /// <summary>An informational finding; not necessarily a defect.</summary>
    Info
}
