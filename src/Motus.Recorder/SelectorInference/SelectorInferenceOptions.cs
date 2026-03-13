namespace Motus.Recorder.SelectorInference;

/// <summary>
/// Configuration for <see cref="SelectorInferenceEngine"/>.
/// </summary>
public sealed class SelectorInferenceOptions
{
    /// <summary>Maximum character length for a generated selector before it is discarded.</summary>
    public int MaxSelectorLength { get; init; } = 200;

    /// <summary>Maximum time allowed for the full inference pipeline per action.</summary>
    public TimeSpan InferenceTimeout { get; init; } = TimeSpan.FromSeconds(2);
}
