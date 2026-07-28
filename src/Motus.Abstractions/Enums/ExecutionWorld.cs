namespace Motus.Abstractions;

/// <summary>
/// Specifies which JavaScript world an expression is evaluated in.
/// </summary>
public enum ExecutionWorld
{
    /// <summary>
    /// The world the page's own scripts run in. Application globals are visible here, and so is
    /// anything the page has altered about the DOM APIs.
    /// </summary>
    Main,

    /// <summary>
    /// A world of its own, sharing the document but not its globals. The DOM is fully present and
    /// application globals are not, which makes this the safer choice for querying and the wrong
    /// choice for reading application state.
    /// </summary>
    Isolated
}
