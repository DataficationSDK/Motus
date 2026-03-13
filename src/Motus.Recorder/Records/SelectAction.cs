namespace Motus.Recorder.Records;

/// <summary>
/// A select element change (one or more options selected).
/// </summary>
public sealed record SelectAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string[] SelectedValues
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
