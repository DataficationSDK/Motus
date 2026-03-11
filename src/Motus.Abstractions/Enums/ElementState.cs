namespace Motus.Abstractions;

/// <summary>
/// Specifies the expected state of an element when waiting.
/// </summary>
public enum ElementState
{
    /// <summary>Element is present in the DOM and visible.</summary>
    Visible,

    /// <summary>Element is present in the DOM but not visible.</summary>
    Hidden,

    /// <summary>Element is present in the DOM.</summary>
    Attached,

    /// <summary>Element is not present in the DOM.</summary>
    Detached
}
