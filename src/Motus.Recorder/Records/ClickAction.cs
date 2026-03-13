namespace Motus.Recorder.Records;

/// <summary>
/// A mouse click action (mousedown + mouseup within threshold).
/// </summary>
public sealed record ClickAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string Button,
    int ClickCount,
    int Modifiers
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
