namespace Motus.Selectors;

/// <summary>
/// Snapshot of a DOM element's identifying properties, captured when a selector is recorded.
/// Used by selector health/repair tooling to locate the same element after DOM changes.
/// </summary>
internal sealed record DomFingerprint(
    string TagName,
    IReadOnlyDictionary<string, string> KeyAttributes,
    string? VisibleText,
    string AncestorPath,
    string Hash);
