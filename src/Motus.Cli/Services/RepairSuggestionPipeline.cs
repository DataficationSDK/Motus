using Motus.Abstractions;
using Motus.Selectors;

namespace Motus.Cli.Services;

/// <summary>
/// Produces a ranked list of repair suggestions for a broken selector whose
/// manifest fingerprint was matched against a live element. Iterates all
/// registered <see cref="ISelectorStrategy"/> instances in priority order,
/// validates each candidate for uniqueness, and falls back to attribute-based
/// suggestions when no strategy produces a stable selector.
/// </summary>
internal static class RepairSuggestionPipeline
{
    private static readonly string[] KeyAttributes =
        ["id", "name", "role", "data-testid", "aria-label", "type", "href"];

    internal static async Task<IReadOnlyList<RepairSuggestion>> BuildAsync(
        IPage page, DomFingerprint fingerprint, FingerprintMatch match, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(match);

        var confidence = ConfidenceMapping.FromQuality(match.Quality);
        var suggestions = new List<RepairSuggestion>();

        var handle = await ResolveElementHandleAsync(page, fingerprint, match.Candidate, ct)
            .ConfigureAwait(false);

        if (handle is not null)
        {
            var strategies = GetStrategies(page);
            foreach (var strategy in strategies)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var raw = await strategy.GenerateSelector(handle, ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(raw))
                        continue;

                    var matches = await strategy.ResolveAsync(raw, page.MainFrame, pierceShadow: true, ct)
                        .ConfigureAwait(false);
                    if (matches.Count != 1)
                        continue;

                    var replacement = TranslateToLocatorCall(strategy.StrategyName, raw);
                    suggestions.Add(new RepairSuggestion(replacement, strategy.StrategyName, confidence));
                }
                catch
                {
                    // Strategy failed; try the next.
                }
            }
        }

        if (suggestions.Count == 0)
        {
            // Attribute-derived fallback: preserves pre-3D behavior when no strategy
            // generated a uniquely-matching selector (or when the handle could not be
            // resolved at all, e.g. pre-filter returned zero live elements).
            suggestions.Add(new RepairSuggestion(
                SuggestionBuilder.Build(match.Candidate),
                StrategyName: "fallback",
                Confidence: confidence));
        }

        return suggestions;
    }

    private static IReadOnlyList<ISelectorStrategy> GetStrategies(IPage page)
    {
        // SelectorStrategyRegistry is internal to Motus; Motus.Cli has InternalsVisibleTo
        // which lets us traverse the concrete Page/BrowserContext graph.
        if (page is Page concretePage)
            return concretePage.ContextInternal.SelectorStrategies.GetAllByPriority();
        return Array.Empty<ISelectorStrategy>();
    }

    private static async Task<IElementHandle?> ResolveElementHandleAsync(
        IPage page, DomFingerprint fingerprint, FingerprintCandidate candidate, CancellationToken ct)
    {
        try
        {
            var preFilter = FingerprintScanner.BuildPreFilterSelector(fingerprint);
            var locator = page.Locator("css=" + preFilter);
            var handles = await locator.ElementHandlesAsync().ConfigureAwait(false);
            if (handles.Count == 0)
                return null;
            if (handles.Count == 1)
                return handles[0];

            // Multiple candidates: match on the seven key attributes.
            foreach (var h in handles)
            {
                if (await AttributesMatchAsync(h, candidate.KeyAttributes, ct).ConfigureAwait(false))
                    return h;
            }
            return handles[0];
        }
        catch
        {
            return null;
        }
    }

    private static async Task<bool> AttributesMatchAsync(
        IElementHandle handle, IReadOnlyDictionary<string, string> expected, CancellationToken ct)
    {
        foreach (var name in KeyAttributes)
        {
            var expectedValue = expected.TryGetValue(name, out var v) ? v : null;
            var actual = await handle.GetAttributeAsync(name, ct).ConfigureAwait(false);
            if (!string.Equals(expectedValue, actual, StringComparison.Ordinal))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Converts a raw strategy-produced selector (e.g. <c>data-testid=submit</c>) into
    /// a Motus locator-call fragment (e.g. <c>GetByTestId("submit")</c>). Falls back
    /// to <c>Locator(...)</c> when the strategy name does not map to a factory method.
    /// </summary>
    internal static string TranslateToLocatorCall(string strategyName, string rawSelector)
    {
        var prefix = strategyName + "=";
        var value = rawSelector.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? rawSelector[prefix.Length..]
            : rawSelector;

        if (string.Equals(strategyName, "role", StringComparison.OrdinalIgnoreCase))
        {
            var (role, name) = RoleSelectorStrategy.ParseRoleSelector(rawSelector);
            return name is null
                ? $"GetByRole(\"{Escape(role)}\")"
                : $"GetByRole(\"{Escape(role)}\", name: \"{Escape(name)}\")";
        }

        if (string.Equals(strategyName, "text", StringComparison.OrdinalIgnoreCase))
            return $"GetByText(\"{Escape(value)}\")";

        // The default TestIdSelectorStrategy's StrategyName is the attribute name, which is
        // "data-testid" out-of-box but is configurable. Treat the canonical "data-testid"
        // as GetByTestId; otherwise fall through to Locator() with the prefixed form so
        // the configured registry still resolves it.
        if (string.Equals(strategyName, "data-testid", StringComparison.OrdinalIgnoreCase))
            return $"GetByTestId(\"{Escape(value)}\")";

        // css, xpath, or a custom-prefixed strategy: preserve the prefix so
        // Locator() dispatches to the same strategy.
        return $"Locator(\"{Escape(rawSelector)}\")";
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
