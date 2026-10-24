using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in ARIA role selector strategy. Prefix: role=
/// Supports optional name filter: role=button[name="Submit"]
/// Uses CDP Accessibility domain for resolution.
/// </summary>
internal sealed class RoleSelectorStrategy : ISelectorStrategy
{
    private volatile bool _accessibilityEnabled;

    public string StrategyName => "role";

    public int Priority => 30;

    // CDP Accessibility.queryAXTree already traverses shadow boundaries natively.
    //
    // This strategy scopes by node rather than by execution context, because queryAXTree takes no
    // context and refuses a query that names no root node at all. So it always hands over the
    // document element of the frame the locator belongs to, the main frame included for a
    // page-level locator. That is the same document the other strategies query, and the query
    // stays inside it, so a match in a nested frame needs a locator built on that frame.
    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var session = page.SessionFor(frame);
        CapabilityGuard.RequireAccessibilityTree(session, "GetByRole and role= selectors",
            "Match by label, text, test id or CSS selector instead.");

        var (role, name) = ParseRoleSelector(selector);

        if (!_accessibilityEnabled)
        {
            await session.SendAsync(
                "Accessibility.enable",
                CdpJsonContext.Default.AccessibilityEnableResult,
                ct).ConfigureAwait(false);
            _accessibilityEnabled = true;
        }

        var rootObjectId = await SelectorStrategyHelpers
            .ResolveFrameDocumentObjectIdAsync(frame, ct).ConfigureAwait(false);

        // A frame with no reachable document has nothing to match, which is the same answer the
        // other strategies give when their evaluate comes back with nothing. Sending the query
        // without a root instead would fail the whole call on the protocol.
        if (rootObjectId is null)
            return [];

        // The query matches the browser's own spelling of a role, and Chromium spells the ARIA
        // role "img" as "image", for an <img> and for an explicit role="img" alike. Each spelling
        // of the image role is tried in turn, so either one finds images on any Chromium build.
        var nodes = Array.Empty<AccessibilityAXNodeSimple>();
        foreach (var spelling in QueryRoles(role))
        {
            var queryResult = await session.SendAsync(
                "Accessibility.queryAXTree",
                new AccessibilityQueryAXTreeParams(
                    ObjectId: rootObjectId,
                    AccessibleName: name,
                    Role: spelling),
                CdpJsonContext.Default.AccessibilityQueryAXTreeParams,
                CdpJsonContext.Default.AccessibilityQueryAXTreeResult,
                ct).ConfigureAwait(false);

            nodes = queryResult.Nodes;
            if (nodes.Length > 0)
                break;
        }

        var handles = new List<IElementHandle>();
        foreach (var node in nodes)
        {
            if (node.Ignored || node.BackendDOMNodeId is null)
                continue;

            var handle = await SelectorStrategyHelpers.ResolveNodeToHandleAsync(
                frame, node.BackendDOMNodeId.Value, ct).ConfigureAwait(false);
            handles.Add(handle);
        }

        return handles;
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var result = await element.EvaluateAsync<string?>(
            """
            function() {
                var el = this;
                var role = el.getAttribute('role');
                if (!role) {
                    var tag = el.tagName.toLowerCase();
                    var type = (el.getAttribute('type') || '').toLowerCase();
                    if (tag === 'button' || (tag === 'input' && (type === 'submit' || type === 'button' || type === 'reset')))
                        role = 'button';
                    else if (tag === 'a' && el.hasAttribute('href'))
                        role = 'link';
                    else if (tag === 'input' && type === 'checkbox')
                        role = 'checkbox';
                    else if (tag === 'input' && type === 'radio')
                        role = 'radio';
                    else if (tag === 'select')
                        role = 'listbox';
                    else if (tag === 'textarea')
                        role = 'textbox';
                    else if (tag === 'input' && (type === '' || type === 'text' || type === 'email' || type === 'password' || type === 'search' || type === 'url' || type === 'tel' || type === 'number'))
                        role = 'textbox';
                }
                if (!role) return null;
                var name = el.getAttribute('aria-label') || el.textContent?.trim();
                if (name && name.length <= 100) return role + '\t' + name;
                return role + '\t';
            }
            """).ConfigureAwait(false);

        if (result is null)
            return null;

        var tabIdx = result.IndexOf('\t');
        if (tabIdx < 0)
            return $"role={result}";

        var role = result[..tabIdx];
        var name = result[(tabIdx + 1)..];

        if (name.Length > 0)
            return $"""role={role}[name="{name}"]""";

        return $"role={role}";
    }

    /// <summary>
    /// The role spellings to query for a role, in order. Every role but the image role has one.
    /// </summary>
    internal static IReadOnlyList<string> QueryRoles(string role)
        => AccessibilityRoleNames.IsImage(role) ? ["image", "img"] : [role];

    /// <summary>
    /// Builds a role selector, with a name filter when a name is given. The name is written as is:
    /// <see cref="ParseRoleSelector"/> takes everything between <c>name="</c> and the closing
    /// <c>"]</c>, so quotes, brackets and backslashes inside the name need no escaping.
    /// </summary>
    internal static string BuildSelector(string role, string? name)
        => name is null ? $"role={role}" : $"role={role}[name=\"{name}\"]";

    /// <summary>
    /// Parses role=button[name="Submit"] into (role, name) using span slicing (no Regex).
    /// </summary>
    internal static (string role, string? name) ParseRoleSelector(ReadOnlySpan<char> selector)
    {
        // Strip the "role=" prefix if present
        if (selector.StartsWith("role="))
            selector = selector["role=".Length..];

        var bracketStart = selector.IndexOf('[');
        if (bracketStart < 0)
            return (selector.ToString(), null);

        var role = selector[..bracketStart].ToString();

        var rest = selector[(bracketStart + 1)..];
        if (!rest.StartsWith("name="))
            return (role, null);

        rest = rest["name=".Length..];

        // Strip surrounding quotes if present
        if (rest.Length >= 2 && rest[0] == '"' && rest[^1] == ']')
        {
            var nameSpan = rest[1..^2]; // skip leading " and trailing "]
            return (role, nameSpan.ToString());
        }

        // Unquoted or malformed
        var endBracket = rest.IndexOf(']');
        if (endBracket >= 0)
            return (role, rest[..endBracket].ToString());

        return (role, rest.ToString());
    }
}
