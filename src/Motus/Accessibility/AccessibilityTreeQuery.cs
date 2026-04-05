using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Fetches and parses the full accessibility tree for a page via CDP.
/// </summary>
internal sealed class AccessibilityTreeQuery
{
    private readonly IMotusSession _session;

    internal AccessibilityTreeQuery(IMotusSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Returns the root nodes of the walkable accessibility tree plus a flat list of all nodes.
    /// Ignored nodes are excluded from the returned tree but their count is tracked.
    /// For non-CDP transports, returns an empty tree with a diagnostic message.
    /// </summary>
    internal async Task<AccessibilityTreeResult> GetTreeAsync(CancellationToken ct)
    {
        if ((_session.Capabilities & MotusCapabilities.AccessibilityTree) == 0)
        {
            return new AccessibilityTreeResult(
                Roots: [],
                AllWalkableNodes: [],
                IgnoredCount: 0,
                DiagnosticMessage: "Accessibility.getFullAXTree is not supported on the active transport (" +
                                   CapabilityGuard.GetTransportDescription(_session) +
                                   "). Use a Chromium-based browser for accessibility audits.");
        }

        await _session.SendAsync(
            "Accessibility.enable",
            CdpJsonContext.Default.AccessibilityEnableResult,
            ct).ConfigureAwait(false);

        var result = await _session.SendAsync(
            "Accessibility.getFullAXTree",
            new AccessibilityGetFullAXTreeParams(),
            CdpJsonContext.Default.AccessibilityGetFullAXTreeParams,
            CdpJsonContext.Default.AccessibilityGetFullAXTreeResult,
            ct).ConfigureAwait(false);

        return BuildTree(result.Nodes);
    }

    private static AccessibilityTreeResult BuildTree(AccessibilityAXNode[] rawNodes)
    {
        var byId = new Dictionary<string, AccessibilityAXNode>(rawNodes.Length);
        foreach (var n in rawNodes)
            byId[n.NodeId] = n;

        int ignoredCount = 0;

        // First pass: convert non-ignored CDP nodes to public AccessibilityNode records
        var converted = new Dictionary<string, AccessibilityNode>(rawNodes.Length);
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored)
            {
                ignoredCount++;
                continue;
            }

            var props = BuildProperties(raw.Properties);
            converted[raw.NodeId] = new AccessibilityNode(
                NodeId: raw.NodeId,
                Role: ExtractString(raw.Role),
                Name: ExtractString(raw.Name),
                Value: ExtractString(raw.Value),
                Description: ExtractString(raw.Description),
                Properties: props,
                Children: [],
                BackendDOMNodeId: raw.BackendDOMNodeId,
                Ignored: false);
        }

        // Second pass: wire ChildIds into actual Children lists
        var withChildren = new Dictionary<string, AccessibilityNode>(converted.Count);
        foreach (var (nodeId, raw) in byId)
        {
            if (raw.Ignored || !converted.TryGetValue(nodeId, out var node))
                continue;

            var childList = new List<AccessibilityNode>();
            if (raw.ChildIds is not null)
            {
                foreach (var childId in raw.ChildIds)
                {
                    if (converted.TryGetValue(childId, out var childNode))
                        childList.Add(childNode);
                }
            }

            withChildren[nodeId] = node with { Children = childList };
        }

        // Find root nodes: nodes not referenced as children of any other walkable node
        var childIds = new HashSet<string>();
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored || raw.ChildIds is null)
                continue;
            foreach (var id in raw.ChildIds)
                childIds.Add(id);
        }

        var roots = new List<AccessibilityNode>();
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored)
                continue;
            if (!childIds.Contains(raw.NodeId) && withChildren.TryGetValue(raw.NodeId, out var rootNode))
                roots.Add(rootNode);
        }

        // Flat list in depth-first order
        var all = new List<AccessibilityNode>(withChildren.Count);
        var visited = new HashSet<string>();
        foreach (var root in roots)
            CollectDepthFirst(root, all, visited);

        return new AccessibilityTreeResult(
            Roots: roots,
            AllWalkableNodes: all,
            IgnoredCount: ignoredCount,
            DiagnosticMessage: null);
    }

    private static string? ExtractString(AccessibilityAXValue? val) =>
        val?.Value is { ValueKind: JsonValueKind.String } el ? el.GetString() : null;

    private static IReadOnlyDictionary<string, string?> BuildProperties(
        AccessibilityAXProperty[]? rawProps)
    {
        if (rawProps is null or { Length: 0 })
            return new Dictionary<string, string?>();

        var dict = new Dictionary<string, string?>(rawProps.Length);
        foreach (var p in rawProps)
        {
            string? strVal = p.Value.Value is { ValueKind: JsonValueKind.String } el
                ? el.GetString()
                : p.Value.Value?.ToString();
            dict[p.Name] = strVal;
        }
        return dict;
    }

    private static void CollectDepthFirst(
        AccessibilityNode node, List<AccessibilityNode> list, HashSet<string> visited)
    {
        if (!visited.Add(node.NodeId))
            return;

        list.Add(node);
        foreach (var child in node.Children)
            CollectDepthFirst(child, list, visited);
    }
}

/// <summary>
/// Internal result of a tree fetch, before audit execution.
/// </summary>
internal sealed record AccessibilityTreeResult(
    IReadOnlyList<AccessibilityNode> Roots,
    IReadOnlyList<AccessibilityNode> AllWalkableNodes,
    int IgnoredCount,
    string? DiagnosticMessage);
