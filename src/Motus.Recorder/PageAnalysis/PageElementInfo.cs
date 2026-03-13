namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Raw element data returned by the DOM crawl JavaScript evaluation.
/// </summary>
public sealed record PageElementInfo(
    string Tag,
    string? Type,
    string? Id,
    string? Name,
    string? AriaLabel,
    string? Placeholder,
    string? Text,
    string? Href,
    string? Role,
    string? DataTestId,
    int? FormIndex,
    int ElementIndex);
