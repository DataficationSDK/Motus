using System.Collections.Concurrent;

namespace Motus;

/// <summary>
/// Manages CDP sessions for multiplexing commands across browser targets
/// over a single WebSocket connection.
/// </summary>
internal sealed class CdpSessionRegistry : IMotusSessionRegistry
{
    private readonly CdpTransport _transport;
    private readonly ConcurrentDictionary<string, CdpSession> _sessions = new();

    /// <inheritdoc />
    public IMotusSession BrowserSession { get; }

    internal CdpSessionRegistry(CdpTransport transport)
    {
        _transport = transport;
        BrowserSession = new CdpSession(transport, sessionId: null);
    }

    /// <inheritdoc />
    public IMotusSession CreateSession(string sessionId)
    {
        var session = new CdpSession(_transport, sessionId);
        _sessions[sessionId] = session;
        return session;
    }

    /// <inheritdoc />
    public bool TryGetSession(string sessionId, out IMotusSession? session)
    {
        var found = _sessions.TryGetValue(sessionId, out var cdpSession);
        session = cdpSession;
        return found;
    }

    /// <inheritdoc />
    public bool RemoveSession(string sessionId)
        => _sessions.TryRemove(sessionId, out _);

    /// <summary>
    /// Returns all currently active sessions (excluding the browser session).
    /// </summary>
    internal IReadOnlyCollection<CdpSession> ActiveSessions =>
        (IReadOnlyCollection<CdpSession>)_sessions.Values;
}
