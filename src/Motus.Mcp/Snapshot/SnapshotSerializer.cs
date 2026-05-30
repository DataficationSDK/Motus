using System.Text;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// The serialized form of an accessibility snapshot: the indented text the agent
/// reads, paired with the map from each assigned ref to the backend DOM node it
/// addresses.
/// </summary>
internal sealed record SerializedSnapshot(
    string Text,
    IReadOnlyDictionary<string, long> RefToBackendNodeId);

/// <summary>
/// Renders an <see cref="AccessibilitySnapshot"/> into a compact, indented ARIA
/// text tree and assigns each addressable node a ref (e1, e2, ...) in document
/// order. Pure and stateless: the same snapshot always renders identically.
/// </summary>
internal static class SnapshotSerializer
{
    // Boolean accessibility properties surfaced as [state] flags, in render order.
    private static readonly string[] StateProperties =
        ["disabled", "readonly", "required", "checked", "selected", "expanded", "pressed"];

    public static SerializedSnapshot Serialize(AccessibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var builder = new StringBuilder();
        var refMap = new Dictionary<string, long>(StringComparer.Ordinal);
        var nextRef = 1;

        foreach (var root in snapshot.Roots)
            nextRef = WriteNode(root, depth: 0, builder, refMap, nextRef);

        return new SerializedSnapshot(builder.ToString(), refMap);
    }

    private static int WriteNode(
        AccessibilityNode node, int depth, StringBuilder builder,
        Dictionary<string, long> refMap, int nextRef)
    {
        builder.Append(' ', depth * 2).Append("- ");
        builder.Append(string.IsNullOrEmpty(node.Role) ? "generic" : node.Role);

        if (!string.IsNullOrEmpty(node.Name))
            builder.Append(' ').Append('"').Append(node.Name).Append('"');

        if (node.BackendDOMNodeId is { } backendNodeId)
        {
            var refId = $"e{nextRef++}";
            refMap[refId] = backendNodeId;
            builder.Append(" [ref=").Append(refId).Append(']');
        }

        if (!string.IsNullOrEmpty(node.Value))
            builder.Append(" [value=\"").Append(node.Value).Append("\"]");

        foreach (var state in StateProperties)
        {
            if (node.Properties.TryGetValue(state, out var value) &&
                string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append(" [").Append(state).Append(']');
            }
        }

        builder.Append('\n');

        foreach (var child in node.Children)
            nextRef = WriteNode(child, depth + 1, builder, refMap, nextRef);

        return nextRef;
    }
}
