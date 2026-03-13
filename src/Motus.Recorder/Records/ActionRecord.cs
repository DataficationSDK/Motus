namespace Motus.Recorder.Records;

/// <summary>
/// Base record for all captured browser actions.
/// </summary>
public abstract record ActionRecord(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y
);
