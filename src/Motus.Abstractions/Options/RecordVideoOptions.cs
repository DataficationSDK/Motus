namespace Motus.Abstractions;

/// <summary>
/// Options for recording video of page actions.
/// </summary>
public sealed record RecordVideoOptions
{
    /// <summary>Path to the directory where videos will be saved.</summary>
    public required string Dir { get; init; }

    /// <summary>The size of the recorded video. Defaults to the viewport size scaled down to fit 800x800.</summary>
    public ViewportSize? Size { get; init; }
}
