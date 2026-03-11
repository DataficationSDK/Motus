namespace Motus.Abstractions;

/// <summary>
/// Specifies the browser distribution channel to use when launching.
/// </summary>
public enum BrowserChannel
{
    /// <summary>Google Chrome stable channel.</summary>
    Chrome,

    /// <summary>Microsoft Edge stable channel.</summary>
    Edge,

    /// <summary>Open-source Chromium build.</summary>
    Chromium
}
