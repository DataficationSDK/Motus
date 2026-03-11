namespace Motus.Abstractions;

/// <summary>
/// Specifies when a navigation is considered complete.
/// </summary>
public enum WaitUntil
{
    /// <summary>Navigation is complete when the load event fires.</summary>
    Load,

    /// <summary>Navigation is complete when the DOMContentLoaded event fires.</summary>
    DOMContentLoaded,

    /// <summary>Navigation is complete when there are no network connections for at least 500 ms.</summary>
    NetworkIdle
}
