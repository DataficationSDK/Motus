namespace Motus.Recorder.Records;

/// <summary>
/// A scroll action (debounced from rapid scroll events).
/// </summary>
public sealed record ScrollAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    double ScrollX,
    double ScrollY
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
