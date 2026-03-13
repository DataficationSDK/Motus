namespace Motus.Recorder.Records;

/// <summary>
/// A text input action (debounced from rapid keystroke input events).
/// </summary>
public sealed record FillAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string Value
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
