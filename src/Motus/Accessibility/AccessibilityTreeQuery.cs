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
    /// <param name="ct">Cancellation token.</param>
    /// <param name="frameId">
    /// The frame whose document to read. Omitted, the session's own root document is read, which
    /// for a page session includes every frame it hosts. Naming a frame is what narrows the tree to
    /// that frame alone, and it is required rather than optional for a frame in its own process,
    /// whose nodes are not in the page's tree at all.
    /// </param>
    internal async Task<AccessibilityTreeResult> GetTreeAsync(CancellationToken ct, string? frameId = null)
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
            new AccessibilityGetFullAXTreeParams(FrameId: frameId),
            CdpJsonContext.Default.AccessibilityGetFullAXTreeParams,
            CdpJsonContext.Default.AccessibilityGetFullAXTreeResult,
            ct).ConfigureAwait(false);

        return BuildTree(result.Nodes);
    }

    /// <summary>
    /// Turns the browser's flat node list into a tree of walkable nodes: ignored nodes are
    /// dropped and their descendants take their place under the nearest ancestor that survived.
    /// </summary>
    internal static AccessibilityTreeResult BuildTree(AccessibilityAXNode[] rawNodes)
    {
        var byId = new Dictionary<string, AccessibilityAXNode>(rawNodes.Length);
        foreach (var n in rawNodes)
            byId[n.NodeId] = n;

        int ignoredCount = 0;

        // First pass: convert non-ignored CDP nodes to public AccessibilityNode records
        var converted = new Dictionary<string, AccessibilityNode>(rawNodes.Length);
        var childrenById = new Dictionary<string, List<AccessibilityNode>>(rawNodes.Length);
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored)
            {
                ignoredCount++;
                continue;
            }

            var children = new List<AccessibilityNode>();
            childrenById[raw.NodeId] = children;
            var props = BuildProperties(raw.Properties);
            converted[raw.NodeId] = new AccessibilityNode(
                NodeId: raw.NodeId,
                Role: ExtractString(raw.Role),
                Name: ExtractString(raw.Name),
                Value: ExtractString(raw.Value),
                Description: ExtractString(raw.Description),
                Properties: props,
                Children: children,
                BackendDOMNodeId: raw.BackendDOMNodeId,
                Ignored: false);
        }

        // Populate the lists already held by each node. Copying records here would leave
        // parents pointing to the original child records with empty descendants.
        //
        // A child the browser ignored stands in for its own non-ignored descendants, which take
        // its place in the parent's child list. Browsers ignore a great many wrapper elements
        // that carry no meaning of their own, and skipping such a child outright would detach
        // everything beneath it: a form inside a stack of layout divs, or the text inside a list
        // item, would be left with no parent at all.
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored || !childrenById.TryGetValue(raw.NodeId, out var childList))
                continue;

            AppendChildren(raw, childList, byId, converted, []);
        }

        // Find root nodes: a node the browser did not ignore that has no parent, or whose every
        // ancestor was ignored. Deriving roots from "not named as the child of a walkable node"
        // instead would promote every descendant of an ignored wrapper to the top level.
        var roots = new List<AccessibilityNode>();
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored || !converted.TryGetValue(raw.NodeId, out var rootNode))
                continue;

            if (!HasWalkableAncestor(raw, byId))
                roots.Add(rootNode);
        }

        // Flat list in depth-first order
        var all = new List<AccessibilityNode>(converted.Count);
        var visited = new HashSet<string>();
        foreach (var root in roots)
            CollectDepthFirst(root, all, visited);

        // A node whose parent chain reaches a walkable ancestor that does not name it back is
        // reachable by neither route. The protocol does not produce that, but the audit engine
        // reads the flat list and would quietly stop reporting on anything missing from it, so
        // whatever the walk did not reach becomes a root of its own rather than disappearing.
        foreach (var raw in rawNodes)
        {
            if (raw.Ignored
                || visited.Contains(raw.NodeId)
                || !converted.TryGetValue(raw.NodeId, out var stranded))
            {
                continue;
            }

            roots.Add(stranded);
            CollectDepthFirst(stranded, all, visited);
        }

        return new AccessibilityTreeResult(
            Roots: roots,
            AllWalkableNodes: all,
            IgnoredCount: ignoredCount,
            DiagnosticMessage: null);
    }

    /// <summary>
    /// Appends a node's children to the list the parent already holds, replacing each ignored
    /// child with that child's own non-ignored descendants, recursively, so an ignored node
    /// flattens in place rather than cutting its subtree loose.
    /// </summary>
    private static void AppendChildren(
        AccessibilityAXNode parent,
        List<AccessibilityNode> childList,
        Dictionary<string, AccessibilityAXNode> byId,
        Dictionary<string, AccessibilityNode> converted,
        HashSet<string> visited)
    {
        if (parent.ChildIds is null)
            return;

        foreach (var childId in parent.ChildIds)
        {
            // Trees from the protocol are acyclic. A guard costs nothing, and the failure it
            // rules out would not be an error, it would be a walk that never ends.
            if (!visited.Add(childId))
                continue;

            if (converted.TryGetValue(childId, out var childNode))
                childList.Add(childNode);
            else if (byId.TryGetValue(childId, out var ignoredChild))
                AppendChildren(ignoredChild, childList, byId, converted, visited);
        }
    }

    /// <summary>
    /// Whether any ancestor of the node survived into the walkable tree. A node with none is a
    /// root, because flattening has attached everything else to one.
    /// </summary>
    private static bool HasWalkableAncestor(
        AccessibilityAXNode node, Dictionary<string, AccessibilityAXNode> byId)
    {
        var seen = new HashSet<string> { node.NodeId };
        var parentId = node.ParentId;

        while (parentId is not null && byId.TryGetValue(parentId, out var parent))
        {
            if (!parent.Ignored)
                return true;
            if (!seen.Add(parent.NodeId))
                return false;

            parentId = parent.ParentId;
        }

        return false;
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
