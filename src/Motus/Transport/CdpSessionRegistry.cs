using System.Collections.Concurrent;

namespace Motus;

/// <summary>
/// Manages CDP sessions for multiplexing commands across browser targets
/// over a single WebSocket connection.
/// </summary>
internal sealed class CdpSessionRegistry
{
    private readonly CdpTransport _transport;
    private readonly ConcurrentDictionary<string, CdpSession> _sessions = new();

    /// <summary>
    /// The browser-level session (no session ID). Used for Target domain commands
    /// and other browser-scoped operations.
    /// </summary>
    internal CdpSession BrowserSession { get; }

    internal CdpSessionRegistry(CdpTransport transport)
    {
        _transport = transport;
        BrowserSession = new CdpSession(transport, sessionId: null);
    }

    /// <summary>
    /// Creates and registers a new session for the given target session ID.
    /// Called after <c>Target.attachToTarget</c> returns a session ID.
    /// </summary>
    internal CdpSession CreateSession(string sessionId)
    {
        var session = new CdpSession(_transport, sessionId);
        _sessions[sessionId] = session;
        return session;
    }

    /// <summary>
    /// Attempts to retrieve an existing session by its ID.
    /// </summary>
    internal bool TryGetSession(string sessionId, out CdpSession? session)
        => _sessions.TryGetValue(sessionId, out session);

    /// <summary>
    /// Removes a session from the registry. Called on <c>Target.detachedFromTarget</c>.
    /// </summary>
    internal bool RemoveSession(string sessionId)
        => _sessions.TryRemove(sessionId, out _);

    /// <summary>
    /// Returns all currently active sessions (excluding the browser session).
    /// </summary>
    internal IReadOnlyCollection<CdpSession> ActiveSessions =>
        (IReadOnlyCollection<CdpSession>)_sessions.Values;
}
