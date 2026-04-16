namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Pairs a discovered page element with its inferred selector and derived C# member name.
/// </summary>
/// <param name="Info">Raw element info captured from the DOM crawl.</param>
/// <param name="Selector">Inferred locator string, or null when no unique selector exists.</param>
/// <param name="MemberName">C# member name for the generated POM property.</param>
/// <param name="BackendNodeId">CDP backend node id of the matched element, when resolvable.</param>
public sealed record DiscoveredElement(
    PageElementInfo Info,
    string? Selector,
    string MemberName,
    int? BackendNodeId = null);
