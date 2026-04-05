namespace Motus.Abstractions;

/// <summary>
/// Controls how the accessibility audit hook handles violations.
/// </summary>
public enum AccessibilityMode
{
    /// <summary>Accessibility auditing is disabled.</summary>
    Off,

    /// <summary>Violations are reported but do not fail tests.</summary>
    Warn,

    /// <summary>Error-severity violations cause test failures.</summary>
    Enforce
}
