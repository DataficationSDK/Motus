namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Pairs a discovered page element with its inferred selector and derived C# member name.
/// </summary>
public sealed record DiscoveredElement(
    PageElementInfo Info,
    string? Selector,
    string MemberName);
