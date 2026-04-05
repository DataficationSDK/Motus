namespace Motus.Abstractions;

/// <summary>
/// Provides rules with access to the full tree and the page during an audit.
/// </summary>
/// <param name="AllNodes">All walkable nodes in the accessibility tree, in depth-first order.</param>
/// <param name="Page">The page being audited, for computed-style queries.</param>
/// <param name="ComputedStyles">Pre-fetched computed styles keyed by BackendDOMNodeId.</param>
/// <param name="DuplicateIds">Set of HTML id values that appear more than once in the document.</param>
/// <param name="DocumentLanguage">The lang attribute value of the document element, or null if absent.</param>
public sealed record AccessibilityAuditContext(
    IReadOnlyList<AccessibilityNode> AllNodes,
    IPage Page,
    IReadOnlyDictionary<long, ComputedStyleInfo>? ComputedStyles = null,
    IReadOnlySet<string>? DuplicateIds = null,
    string? DocumentLanguage = null);
