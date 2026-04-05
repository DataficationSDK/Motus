namespace Motus.Abstractions;

/// <summary>
/// Provides rules with access to the full tree and the page during an audit.
/// </summary>
/// <param name="AllNodes">All walkable nodes in the accessibility tree, in depth-first order.</param>
/// <param name="Page">The page being audited, for computed-style queries.</param>
public sealed record AccessibilityAuditContext(
    IReadOnlyList<AccessibilityNode> AllNodes,
    IPage Page);
