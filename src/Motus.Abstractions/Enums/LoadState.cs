namespace Motus.Abstractions;

/// <summary>
/// Specifies the page load state to wait for.
/// </summary>
public enum LoadState
{
    /// <summary>Wait for the load event to fire.</summary>
    Load,

    /// <summary>Wait for the DOMContentLoaded event to fire.</summary>
    DOMContentLoaded,

    /// <summary>Wait until there are no network connections for at least 500 ms.</summary>
    NetworkIdle
}
