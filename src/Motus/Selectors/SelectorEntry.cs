namespace Motus.Selectors;

/// <summary>
/// One recorded selector along with its source location, captured page URL, and a
/// <see cref="DomFingerprint"/> of the element it matched at capture time.
/// </summary>
internal sealed record SelectorEntry(
    string Selector,
    string LocatorMethod,
    string SourceFile,
    int SourceLine,
    string PageUrl,
    DomFingerprint Fingerprint);
