using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Motus.Abstractions;
using Motus.Selectors;

namespace Motus.Cli.Services;

/// <summary>
/// A candidate element matched against a manifest fingerprint.
/// Used to synthesize a suggested replacement selector.
/// </summary>
internal sealed record FingerprintCandidate(
    string TagName,
    Dictionary<string, string> KeyAttributes,
    string? VisibleText,
    string AncestorPath);

/// <summary>
/// Scans a live page for an element whose fingerprint matches a stored
/// <see cref="DomFingerprint"/>. Uses a single <c>page.EvaluateAsync</c> round
/// trip and recomputes hashes on the .NET side via <see cref="DomFingerprintBuilder.ComputeHash"/>
/// so the comparison stays canonical. Best-effort: any failure returns null.
/// </summary>
internal static class FingerprintScanner
{
    private const int MaxCandidates = 20;

    internal static async Task<FingerprintMatch?> FindMatchAsync(
        IPage page, DomFingerprint fingerprint, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ct.ThrowIfCancellationRequested();

        try
        {
            var preFilter = BuildPreFilterSelector(fingerprint);
            var script = BuildScanScript(preFilter);

            var json = await page.EvaluateAsync<string>(script).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
                return null;

            var candidates = JsonSerializer.Deserialize(
                json, FingerprintScannerJsonContext.Default.FingerprintCandidateArray);
            if (candidates is null || candidates.Length == 0)
                return null;

            // Strict pass: recompute SHA-256 with the canonical .NET hasher.
            foreach (var c in candidates)
            {
                var hash = DomFingerprintBuilder.ComputeHash(
                    c.TagName, c.KeyAttributes, c.VisibleText, c.AncestorPath);
                if (string.Equals(hash, fingerprint.Hash, StringComparison.Ordinal))
                    return new FingerprintMatch(c, FingerprintMatchQuality.Hash);
            }

            // Attributes pass: same tag + every manifest key attribute present and equal,
            // OR at least three key attributes match. Tolerates benign outerHTML/whitespace
            // differences between CDP capture and browser live query.
            foreach (var c in candidates)
            {
                if (!string.Equals(c.TagName, fingerprint.TagName, StringComparison.Ordinal))
                    continue;
                if (AllAttributesMatch(fingerprint.KeyAttributes, c.KeyAttributes)
                    || CountMatchingAttributes(fingerprint.KeyAttributes, c.KeyAttributes) >= 3)
                    return new FingerprintMatch(c, FingerprintMatchQuality.Attributes);
            }

            // Ancestor pass: same tag + same ancestor path. Weakest match quality,
            // reserved for pages where the element has been restyled but not moved.
            foreach (var c in candidates)
            {
                if (!string.Equals(c.TagName, fingerprint.TagName, StringComparison.Ordinal))
                    continue;
                if (string.Equals(c.AncestorPath, fingerprint.AncestorPath, StringComparison.Ordinal))
                    return new FingerprintMatch(c, FingerprintMatchQuality.Ancestor);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool AllAttributesMatch(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        if (expected.Count == 0)
            return false;

        foreach (var kvp in expected)
        {
            if (!actual.TryGetValue(kvp.Key, out var val) || val != kvp.Value)
                return false;
        }
        return true;
    }

    private static int CountMatchingAttributes(
        IReadOnlyDictionary<string, string> expected,
        IReadOnlyDictionary<string, string> actual)
    {
        var hits = 0;
        foreach (var kvp in expected)
        {
            if (actual.TryGetValue(kvp.Key, out var val) && val == kvp.Value)
                hits++;
        }
        return hits;
    }

    internal static string BuildPreFilterSelector(DomFingerprint fingerprint)
    {
        if (fingerprint.KeyAttributes.TryGetValue("data-testid", out var testId))
            return $"[data-testid=\"{EscapeAttr(testId)}\"]";
        if (fingerprint.KeyAttributes.TryGetValue("id", out var id))
            return $"[id=\"{EscapeAttr(id)}\"]";
        if (fingerprint.KeyAttributes.TryGetValue("name", out var name))
            return $"{fingerprint.TagName}[name=\"{EscapeAttr(name)}\"]";
        return fingerprint.TagName;
    }

    private static string EscapeAttr(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string BuildScanScript(string preFilter)
    {
        // IIFE returning a JSON string. The JS mirrors DomFingerprintBuilder's
        // inputs: tagName (lowercased localName), the same seven key attributes,
        // visible text (tags stripped / whitespace collapsed / truncated to 100),
        // and the three closest ancestors' tag names joined outermost -> innermost.
        var escapedFilter = preFilter.Replace("\\", "\\\\").Replace("\"", "\\\"");
        return $$"""
            (() => {
                const KEY = ["id","name","role","data-testid","aria-label","type","href"];
                const MAX = {{MaxCandidates}};
                const els = document.querySelectorAll("{{escapedFilter}}");
                const out = [];
                const limit = Math.min(els.length, MAX);
                for (let i = 0; i < limit; i++) {
                    const el = els[i];
                    const attrs = {};
                    for (const k of KEY) {
                        const v = el.getAttribute(k);
                        if (v !== null) attrs[k] = v;
                    }
                    const outer = el.outerHTML || "";
                    let sb = ""; let inTag = false; let lastSpace = true;
                    for (let j = 0; j < outer.length && sb.length < 100; j++) {
                        const c = outer[j];
                        if (c === '<') { inTag = true; continue; }
                        if (c === '>') { inTag = false; continue; }
                        if (inTag) continue;
                        if (/\s/.test(c)) {
                            if (!lastSpace) { sb += ' '; lastSpace = true; }
                            continue;
                        }
                        sb += c; lastSpace = false;
                    }
                    let text = sb.trim();
                    if (text.length > 100) text = text.substring(0, 100);
                    const visibleText = text.length === 0 ? null : text;
                    const ancestors = [];
                    let p = el.parentElement;
                    for (let d = 0; d < 3 && p; d++) {
                        ancestors.unshift(p.localName.toLowerCase());
                        p = p.parentElement;
                    }
                    out.push({
                        tagName: el.localName.toLowerCase(),
                        keyAttributes: attrs,
                        visibleText: visibleText,
                        ancestorPath: ancestors.join(' > ')
                    });
                }
                return JSON.stringify(out);
            })()
            """;
    }
}

[JsonSerializable(typeof(FingerprintCandidate[]))]
[JsonSerializable(typeof(FingerprintCandidate))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[ExcludeFromCodeCoverage]
internal sealed partial class FingerprintScannerJsonContext : JsonSerializerContext;
