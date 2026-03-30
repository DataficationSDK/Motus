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
    public TimeSpan InferenceTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// When true, performs a second analysis pass using DOMDebugger.getEventListeners
    /// to discover elements with directly-attached JS event handlers (vanilla JS, jQuery, etc.).
    /// React event delegation is not captured. Disabled by default.
    /// </summary>
    public bool DetectEventListeners { get; init; }

    /// <summary>
    /// Optional CSS selector to scope element discovery to a specific container
    /// (e.g. ".modal-dialog", "#login-form"). When null, the entire document is crawled.
    /// </summary>
    public string? Scope { get; init; }
}
