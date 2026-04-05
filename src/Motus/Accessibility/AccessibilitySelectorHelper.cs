using System.Text;
using System.Text.Json;

namespace Motus;

/// <summary>
/// Derives a best-effort CSS selector for an element using DOM.describeNode.
/// </summary>
internal static class AccessibilitySelectorHelper
{
    /// <summary>
    /// Attempts to build a CSS selector for the given backend DOM node.
    /// Returns null if the node cannot be described. Never throws.
    /// </summary>
    internal static async Task<string?> TryGetSelectorAsync(
        IMotusSession session, long backendDOMNodeId, CancellationToken ct)
    {
        try
        {
            var result = await session.SendAsync(
                "DOM.describeNode",
                new DomDescribeNodeParams(BackendNodeId: (int)backendDOMNodeId),
                CdpJsonContext.Default.DomDescribeNodeParams,
                CdpJsonContext.Default.DomDescribeNodeResult,
                ct).ConfigureAwait(false);

            return BuildSelector(result.Node);
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildSelector(DomNodeDescription node)
    {
        if (node.LocalName is null)
            return null;

        var sb = new StringBuilder(node.LocalName.ToLowerInvariant());

        // CDP describeNode returns attributes as a flat [name, value, name, value, ...] array.
        if (node.Attributes is { ValueKind: JsonValueKind.Array } attrs)
        {
            var attrArray = attrs.EnumerateArray().ToArray();
            for (int i = 0; i < attrArray.Length - 1; i += 2)
            {
                var name = attrArray[i].GetString();
                var value = attrArray[i + 1].GetString();

                if (name == "id" && !string.IsNullOrEmpty(value))
                {
                    sb.Append('#');
                    sb.Append(value);
                }
                else if (name == "class" && !string.IsNullOrEmpty(value))
                {
                    foreach (var cls in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        sb.Append('.');
                        sb.Append(cls);
                    }
                }
            }
        }

        return sb.ToString();
    }
}
