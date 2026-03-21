namespace Motus;

/// <summary>
/// Internal abstraction over session multiplexing.
/// Both CDP (Target-domain based) and BiDi (browsing context based) implementations
/// will implement this interface.
/// </summary>
internal interface IMotusSessionRegistry
{
    /// <summary>
    /// The browser-level session used for browser-scoped commands.
    /// </summary>
    IMotusSession BrowserSession { get; }

    /// <summary>
    /// Creates and registers a new session for the given target/context ID.
    /// </summary>
    IMotusSession CreateSession(string sessionId);

    /// <summary>
    /// Attempts to retrieve an existing session by its ID.
    /// </summary>
    bool TryGetSession(string sessionId, out IMotusSession? session);

    /// <summary>
    /// Removes a session from the registry.
    /// </summary>
    bool RemoveSession(string sessionId);
}
