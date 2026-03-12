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

    // CDP Accessibility.queryAXTree already traverses shadow boundaries natively
    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);

        var (role, name) = ParseRoleSelector(selector);

        if (!_accessibilityEnabled)
        {
            await page.Session.SendAsync(
                "Accessibility.enable",
                CdpJsonContext.Default.AccessibilityEnableResult,
                ct);
            _accessibilityEnabled = true;
        }

        var queryResult = await page.Session.SendAsync(
            "Accessibility.queryAXTree",
            new AccessibilityQueryAXTreeParams(
                ObjectId: null,
                AccessibleName: name,
                Role: role),
            CdpJsonContext.Default.AccessibilityQueryAXTreeParams,
            CdpJsonContext.Default.AccessibilityQueryAXTreeResult,
            ct);

        var handles = new List<IElementHandle>();
        foreach (var node in queryResult.Nodes)
        {
            if (node.Ignored || node.BackendDOMNodeId is null)
                continue;

            var handle = await SelectorStrategyHelpers.ResolveNodeToHandleAsync(
                page, node.BackendDOMNodeId.Value, ct);
            handles.Add(handle);
        }

        return handles;
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var role = await element.EvaluateAsync<string?>(
            "function() { return this.getAttribute('role'); }");
        if (role is null)
            return null;

        var name = await element.EvaluateAsync<string?>(
            "function() { return this.getAttribute('aria-label') || this.textContent?.trim(); }");

        if (name is not null && name.Length <= 100)
            return $"""role={role}[name="{name}"]""";

        return $"role={role}";
    }

    /// <summary>
    /// Parses role=button[name="Submit"] into (role, name) using span slicing (no Regex).
    /// </summary>
    internal static (string role, string? name) ParseRoleSelector(ReadOnlySpan<char> selector)
    {
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
