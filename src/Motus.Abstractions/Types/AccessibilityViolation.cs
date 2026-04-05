namespace Motus.Abstractions;

/// <summary>
/// Describes a single accessibility rule violation found during an audit.
/// </summary>
/// <param name="RuleId">The identifier of the rule that produced this violation (e.g. "a11y-alt-text").</param>
/// <param name="Severity">The severity of the violation.</param>
/// <param name="Message">A human-readable description of the violation.</param>
/// <param name="NodeRole">The ARIA role of the violating node.</param>
/// <param name="NodeName">The accessible name of the violating node.</param>
/// <param name="BackendDOMNodeId">The backend DOM node ID for element targeting, if available.</param>
/// <param name="Selector">Best-effort CSS selector for the violating element, or null.</param>
public sealed record AccessibilityViolation(
    string RuleId,
    AccessibilityViolationSeverity Severity,
    string Message,
    string? NodeRole,
    string? NodeName,
    long? BackendDOMNodeId,
    string? Selector);
