using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Pre-fetches computed CSS styles for accessibility nodes that need contrast checking.
/// Uses CSS.getComputedStyleForNode via CDP to resolve color and background-color.
/// </summary>
internal static class ComputedStyleCollector
{
    private static readonly HashSet<string> TextBearingRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "heading", "text", "paragraph", "label", "link", "button",
        "menuitem", "menuitemcheckbox", "menuitemradio", "tab",
        "treeitem", "option", "cell", "columnheader", "rowheader",
        "gridcell", "listitem", "caption"
    };

    internal static async Task<Dictionary<long, ComputedStyleInfo>> CollectAsync(
        IMotusSession session,
        IReadOnlyList<AccessibilityNode> nodes,
        CancellationToken ct)
    {
        var result = new Dictionary<long, ComputedStyleInfo>();

        if ((session.Capabilities & MotusCapabilities.AccessibilityTree) == 0)
            return result;

        // Gather text-bearing nodes with BackendDOMNodeIds
        var candidates = new List<(long backendNodeId, AccessibilityNode node)>();
        foreach (var node in nodes)
        {
            if (node.BackendDOMNodeId.HasValue &&
                !string.IsNullOrWhiteSpace(node.Name) &&
                node.Role is not null &&
                TextBearingRoles.Contains(node.Role))
            {
                candidates.Add((node.BackendDOMNodeId.Value, node));
            }
        }

        if (candidates.Count == 0)
            return result;

        try
        {
            // Enable CSS domain
            await session.SendAsync(
                "CSS.enable",
                CdpJsonContext.Default.CssEnableResult,
                ct).ConfigureAwait(false);

            // Push backend node IDs to get DOM node IDs
            var backendIds = candidates.Select(c => (int)c.backendNodeId).ToArray();
            var pushResult = await session.SendAsync(
                "DOM.pushNodesByBackendIds",
                new DomPushNodesByBackendIdsParams(backendIds),
                CdpJsonContext.Default.DomPushNodesByBackendIdsParams,
                CdpJsonContext.Default.DomPushNodesByBackendIdsResult,
                ct).ConfigureAwait(false);

            for (int i = 0; i < candidates.Count && i < pushResult.NodeIds.Length; i++)
            {
                var domNodeId = pushResult.NodeIds[i];
                if (domNodeId <= 0)
                    continue;

                try
                {
                    var styleResult = await session.SendAsync(
                        "CSS.getComputedStyleForNode",
                        new CssGetComputedStyleForNodeParams(domNodeId),
                        CdpJsonContext.Default.CssGetComputedStyleForNodeParams,
                        CdpJsonContext.Default.CssGetComputedStyleForNodeResult,
                        ct).ConfigureAwait(false);

                    string? color = null, bgColor = null, fontSize = null, fontWeight = null;
                    foreach (var prop in styleResult.ComputedStyle)
                    {
                        switch (prop.Name)
                        {
                            case "color": color = prop.Value; break;
                            case "background-color": bgColor = prop.Value; break;
                            case "font-size": fontSize = prop.Value; break;
                            case "font-weight": fontWeight = prop.Value; break;
                        }
                    }

                    result[candidates[i].backendNodeId] = new ComputedStyleInfo(color, bgColor, fontSize, fontWeight);
                }
                catch
                {
                    // Skip nodes whose styles can't be resolved
                }
            }
        }
        catch
        {
            // CSS domain not available or DOM push failed; return partial results
        }

        return result;
    }

}
