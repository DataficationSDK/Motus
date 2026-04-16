namespace Motus.Selectors;

/// <summary>
/// Collection of <see cref="SelectorEntry"/>s emitted alongside a generated test file or
/// page object model (e.g. <c>LoginTest.selectors.json</c> next to <c>LoginTest.cs</c>).
/// </summary>
internal sealed record SelectorManifest(IReadOnlyList<SelectorEntry> Entries);
