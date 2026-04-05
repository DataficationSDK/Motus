namespace Motus.Abstractions;

/// <summary>
/// Represents a single node in the browser's accessibility tree.
/// </summary>
/// <param name="NodeId">The protocol-assigned node identifier.</param>
/// <param name="Role">The ARIA or implicit role (e.g. "button", "img").</param>
/// <param name="Name">The accessible name.</param>
/// <param name="Value">The current value, for form controls.</param>
/// <param name="Description">The accessible description.</param>
/// <param name="Properties">Additional AX properties keyed by property name.</param>
/// <param name="Children">Child nodes in tree order.</param>
/// <param name="BackendDOMNodeId">The backend DOM node ID for element resolution.</param>
/// <param name="Ignored">True if the node was ignored by the browser and excluded from the walkable tree.</param>
public sealed record AccessibilityNode(
    string NodeId,
    string? Role,
    string? Name,
    string? Value,
    string? Description,
    IReadOnlyDictionary<string, string?> Properties,
    IReadOnlyList<AccessibilityNode> Children,
    long? BackendDOMNodeId,
    bool Ignored = false);
