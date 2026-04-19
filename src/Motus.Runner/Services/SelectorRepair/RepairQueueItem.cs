namespace Motus.Runner.Services.SelectorRepair;

public sealed record RepairQueueItem(
    string Selector,
    string LocatorMethod,
    string SourceFile,
    int SourceLine,
    string PageUrl,
    IReadOnlyList<RepairCandidate> Suggestions,
    string? HighlightSelector);
