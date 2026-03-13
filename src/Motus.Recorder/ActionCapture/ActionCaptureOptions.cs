namespace Motus.Recorder.ActionCapture;

/// <summary>
/// Configuration for the action capture engine.
/// </summary>
public sealed class ActionCaptureOptions
{
    /// <summary>
    /// Name of the JS binding registered via Runtime.addBinding.
    /// </summary>
    public string BindingName { get; init; } = "__motus_recorder__";

    /// <summary>
    /// Debounce window for text input events (milliseconds).
    /// Rapid input events within this window collapse into a single FillAction.
    /// </summary>
    public int FillDebounceMs { get; init; } = 100;

    /// <summary>
    /// Debounce window for scroll events (milliseconds).
    /// Rapid scroll events within this window collapse into a single ScrollAction.
    /// </summary>
    public int ScrollDebounceMs { get; init; } = 150;

    /// <summary>
    /// Maximum time between mousedown and mouseup to be considered a click (milliseconds).
    /// </summary>
    public int ClickTimeThresholdMs { get; init; } = 300;

    /// <summary>
    /// Maximum pixel distance between mousedown and mouseup positions
    /// for the pair to be considered a click (drag detection).
    /// </summary>
    public double ClickDistanceThreshold { get; init; } = 10.0;
}
