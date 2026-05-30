using System.Globalization;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Resolves an element by its backend DOM node identifier. Prefix: _node=
/// (for example _node=42). Backend node IDs are obtained from an accessibility
/// snapshot; this strategy maps one back to a live element via DOM.resolveNode.
/// The leading underscore marks a reserved prefix that does not collide with
/// CSS, XPath, text, role, or test-id selectors.
/// </summary>
internal sealed class BackendNodeIdSelectorStrategy : ISelectorStrategy
{
    /// <summary>The selector prefix this strategy handles.</summary>
    internal const string Prefix = "_node";

    public string StrategyName => Prefix;

    // Highest priority of the built-in strategies; this is a precise, single-node
    // lookup and never competes with content-based selectors.
    public int Priority => 100;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        CapabilityGuard.Require(page.Session.Capabilities, MotusCapabilities.AccessibilityTree,
            "Backend node ID selector (DOM.resolveNode)", CapabilityGuard.GetTransportDescription(page.Session));

        var expression = StripPrefix(selector);
        if (!long.TryParse(expression, NumberStyles.Integer, CultureInfo.InvariantCulture, out var backendNodeId))
            throw new InvalidOperationException(
                $"The '{Prefix}' selector requires a numeric backend node ID; received '{expression}'.");

        var handle = await SelectorStrategyHelpers.ResolveNodeToHandleAsync(
            page, backendNodeId, ct).ConfigureAwait(false);

        return [handle];
    }

    // Backend node IDs are ephemeral and bound to a single snapshot, so they are
    // never emitted as a persistent selector for the recorder.
    public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
        => Task.FromResult<string?>(null);

    private static string StripPrefix(string selector)
        => selector.StartsWith($"{Prefix}=", StringComparison.Ordinal)
            ? selector[(Prefix.Length + 1)..]
            : selector;
}
