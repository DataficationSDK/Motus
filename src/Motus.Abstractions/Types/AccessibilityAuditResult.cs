namespace Motus.Abstractions;

/// <summary>
/// The outcome of an accessibility audit over a page's accessibility tree.
/// </summary>
/// <param name="Violations">All unique violations found during the audit.</param>
/// <param name="PassCount">Number of (node, rule) pairs that passed.</param>
/// <param name="ViolationCount">Number of unique violations found.</param>
/// <param name="Duration">Wall-clock time taken to perform the audit.</param>
/// <param name="DiagnosticMessage">
/// Optional message when the audit could not run fully
/// (e.g. transport does not support accessibility queries).
/// </param>
public sealed record AccessibilityAuditResult(
    IReadOnlyList<AccessibilityViolation> Violations,
    int PassCount,
    int ViolationCount,
    TimeSpan Duration,
    string? DiagnosticMessage = null);
