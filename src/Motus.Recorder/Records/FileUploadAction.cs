namespace Motus.Recorder.Records;

/// <summary>
/// A file upload triggered by a file chooser dialog.
/// </summary>
public sealed record FileUploadAction(
    DateTimeOffset Timestamp,
    string PageUrl,
    int? BackendNodeId,
    double? X,
    double? Y,
    string[] FileNames
) : ActionRecord(Timestamp, PageUrl, BackendNodeId, X, Y);
