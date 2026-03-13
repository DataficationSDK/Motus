namespace Motus.Recorder.Records;

/// <summary>
/// A page navigation event (main frame only).
/// </summary>
public sealed record NavigationAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string Url
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
