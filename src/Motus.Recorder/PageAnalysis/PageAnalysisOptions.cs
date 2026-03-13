namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Configuration for <see cref="PageAnalysisEngine"/>.
/// </summary>
public sealed class PageAnalysisOptions
{
    /// <summary>Ordered list of selector strategy names to try (e.g. "testid", "role", "text", "css").</summary>
    public IReadOnlyList<string>? SelectorPriority { get; init; }

    /// <summary>Maximum character length for a generated selector before it is discarded.</summary>
    public int MaxSelectorLength { get; init; } = 200;

    /// <summary>Maximum time allowed for the full analysis per page.</summary>
    public TimeSpan InferenceTimeout { get; init; } = TimeSpan.FromSeconds(30);
}
