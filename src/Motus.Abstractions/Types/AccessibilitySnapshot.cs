namespace Motus.Abstractions;

/// <summary>
/// The result of fetching a page's accessibility tree. Carries the root nodes
/// (each with its children, forming the full tree), the number of nodes the
/// browser excluded as not accessibility-relevant, and an optional diagnostic
/// message when a snapshot could not be produced (for example on a transport
/// that does not expose an accessibility tree).
/// </summary>
public sealed record AccessibilitySnapshot(
    IReadOnlyList<AccessibilityNode> Roots,
    int IgnoredCount,
    string? DiagnosticMessage);
