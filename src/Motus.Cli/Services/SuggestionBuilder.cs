namespace Motus.Cli.Services;

/// <summary>
/// Produces a human-readable replacement-locator string for a broken selector,
/// given the matched element's fingerprint attributes. Preference order:
/// data-testid, id, aria-label, role+name, visible text, bare tag name.
/// </summary>
internal static class SuggestionBuilder
{
    internal static string Build(FingerprintCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var attrs = candidate.KeyAttributes;

        if (attrs.TryGetValue("data-testid", out var testId))
            return $"GetByTestId(\"{Escape(testId)}\")";

        if (attrs.TryGetValue("id", out var id))
            return $"Locator(\"#{Escape(id)}\")";

        if (attrs.TryGetValue("aria-label", out var label))
            return $"GetByLabel(\"{Escape(label)}\")";

        if (attrs.TryGetValue("role", out var role))
        {
            return candidate.VisibleText is not null
                ? $"GetByRole(\"{Escape(role)}\", name: \"{Escape(candidate.VisibleText)}\")"
                : $"GetByRole(\"{Escape(role)}\")";
        }

        if (!string.IsNullOrEmpty(candidate.VisibleText))
            return $"GetByText(\"{Escape(candidate.VisibleText)}\")";

        return $"Locator(\"{Escape(candidate.TagName)}\")";
    }

    private static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
