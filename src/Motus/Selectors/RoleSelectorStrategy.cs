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
    // context. For a frame root it is scoped to that frame's document element. For the page
    // default it stays unscoped, which means it is the one strategy that reaches into child
    // frames: an accessibility tree spans them, where document.querySelectorAll never does.
    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var session = page.SessionFor(frame);
        CapabilityGuard.Require(session.Capabilities, MotusCapabilities.AccessibilityTree,
            "Role selector (Accessibility.queryAXTree)", CapabilityGuard.GetTransportDescription(session));

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

        var queryResult = await session.SendAsync(
            "Accessibility.queryAXTree",
            new AccessibilityQueryAXTreeParams(
                ObjectId: rootObjectId,
                AccessibleName: name,
                Role: role),
            CdpJsonContext.Default.AccessibilityQueryAXTreeParams,
            CdpJsonContext.Default.AccessibilityQueryAXTreeResult,
            ct).ConfigureAwait(false);

        var handles = new List<IElementHandle>();
        foreach (var node in queryResult.Nodes)
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
