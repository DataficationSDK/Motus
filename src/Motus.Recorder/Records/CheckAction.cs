namespace Motus.Recorder.Records;

/// <summary>
/// A checkbox or radio button change.
/// </summary>
public sealed record CheckAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    bool Checked
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
