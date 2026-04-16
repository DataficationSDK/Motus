using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Motus.Selectors;

/// <summary>
/// Builds a <see cref="DomFingerprint"/> for a DOM element via CDP (DOM.describeNode,
/// DOM.getAttributes, DOM.getOuterHTML). Used when recording/codegen captures a selector
/// so later phases can identify the same element even if the selector has drifted.
/// </summary>
internal static class DomFingerprintBuilder
{
    private static readonly string[] KeyAttributeNames =
    {
        "id", "name", "role", "data-testid", "aria-label", "type", "href"
    };

    private const int MaxAncestorDepth = 3;
    private const int MaxVisibleTextLength = 100;

    /// <summary>
    /// Attempts to build a <see cref="DomFingerprint"/> for the element identified by
    /// <paramref name="backendNodeId"/>. Returns <c>null</c> on any failure - callers
    /// should treat fingerprinting as best-effort.
    /// </summary>
    internal static async Task<DomFingerprint?> TryBuildAsync(
        IMotusSession session, int backendNodeId, CancellationToken ct)
    {
        try
        {
            return await BuildAsync(session, backendNodeId, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<DomFingerprint?> BuildAsync(
        IMotusSession session, int backendNodeId, CancellationToken ct)
    {
        var describe = await session.SendAsync(
            "DOM.describeNode",
            new DomDescribeNodeParams(BackendNodeId: backendNodeId),
            CdpJsonContext.Default.DomDescribeNodeParams,
            CdpJsonContext.Default.DomDescribeNodeResult,
            ct).ConfigureAwait(false);

        var node = describe.Node;
        if (node.LocalName is null)
            return null;

        var tagName = node.LocalName.ToLowerInvariant();
        var keyAttributes = ExtractKeyAttributes(node.Attributes);
        var visibleText = await TryGetVisibleTextAsync(session, backendNodeId, ct).ConfigureAwait(false);
        var ancestorPath = await BuildAncestorPathAsync(session, node.ParentId, ct).ConfigureAwait(false);
        var hash = ComputeHash(tagName, keyAttributes, visibleText, ancestorPath);

        return new DomFingerprint(tagName, keyAttributes, visibleText, ancestorPath, hash);
    }

    private static Dictionary<string, string> ExtractKeyAttributes(JsonElement? attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        if (attributes is not { ValueKind: JsonValueKind.Array } attrs)
            return result;

        var attrArray = attrs.EnumerateArray().ToArray();
        for (int i = 0; i < attrArray.Length - 1; i += 2)
        {
            var name = attrArray[i].GetString();
            var value = attrArray[i + 1].GetString();

            if (name is null || value is null)
                continue;

            if (Array.IndexOf(KeyAttributeNames, name) >= 0)
                result[name] = value;
        }

        return result;
    }

    private static async Task<string?> TryGetVisibleTextAsync(
        IMotusSession session, int backendNodeId, CancellationToken ct)
    {
        try
        {
            var result = await session.SendAsync(
                "DOM.getOuterHTML",
                new DomGetOuterHtmlParams(BackendNodeId: backendNodeId),
                CdpJsonContext.Default.DomGetOuterHtmlParams,
                CdpJsonContext.Default.DomGetOuterHtmlResult,
                ct).ConfigureAwait(false);

            return ExtractAndTruncateText(result.OuterHTML);
        }
        catch
        {
            return null;
        }
    }

    internal static string? ExtractAndTruncateText(string? outerHtml)
    {
        if (string.IsNullOrEmpty(outerHtml))
            return null;

        var sb = new StringBuilder();
        var inTag = false;
        var lastWasSpace = true;

        foreach (var c in outerHtml)
        {
            if (c == '<')
            {
                inTag = true;
                continue;
            }
            if (c == '>')
            {
                inTag = false;
                continue;
            }
            if (inTag)
                continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            sb.Append(c);
            lastWasSpace = false;

            if (sb.Length >= MaxVisibleTextLength)
                break;
        }

        var text = sb.ToString().Trim();
        if (text.Length == 0)
            return null;

        return text.Length > MaxVisibleTextLength
            ? text[..MaxVisibleTextLength]
            : text;
    }

    private static async Task<string> BuildAncestorPathAsync(
        IMotusSession session, int? startParentId, CancellationToken ct)
    {
        var tags = new List<string>(MaxAncestorDepth);
        var parentId = startParentId;

        for (int i = 0; i < MaxAncestorDepth && parentId is not null && parentId.Value > 0; i++)
        {
            DomDescribeNodeResult describe;
            try
            {
                describe = await session.SendAsync(
                    "DOM.describeNode",
                    new DomDescribeNodeParams(NodeId: parentId.Value),
                    CdpJsonContext.Default.DomDescribeNodeParams,
                    CdpJsonContext.Default.DomDescribeNodeResult,
                    ct).ConfigureAwait(false);
            }
            catch
            {
                break;
            }

            var name = describe.Node.LocalName;
            if (string.IsNullOrEmpty(name))
                break;

            tags.Insert(0, name.ToLowerInvariant());
            parentId = describe.Node.ParentId;
        }

        return string.Join(" > ", tags);
    }

    internal static string ComputeHash(
        string tagName,
        IReadOnlyDictionary<string, string> keyAttributes,
        string? visibleText,
        string ancestorPath)
    {
        var sb = new StringBuilder();
        sb.Append(tagName).Append('|');

        foreach (var kvp in keyAttributes.OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            sb.Append(kvp.Key).Append('=').Append(kvp.Value).Append(';');
        }
        sb.Append('|');
        sb.Append(visibleText ?? string.Empty).Append('|');
        sb.Append(ancestorPath);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }
}
