namespace Motus.Recorder.PomEmit;

/// <summary>
/// Configuration for <see cref="PomEmitter"/>.
/// </summary>
public sealed class PomEmitOptions
{
    /// <summary>Namespace for the generated page object class.</summary>
    public string Namespace { get; init; } = "Motus.Generated";

    /// <summary>Class name for the generated page object.</summary>
    public string ClassName { get; init; } = "GeneratedPage";

    /// <summary>Page URL used for the NavigateAsync method.</summary>
    public string PageUrl { get; init; } = "";
}
