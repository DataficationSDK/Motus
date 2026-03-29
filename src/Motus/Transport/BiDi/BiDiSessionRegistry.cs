using System.Collections.Concurrent;

namespace Motus;

/// <summary>
/// Manages BiDi sessions keyed by browsing context ID.
/// Implements <see cref="IMotusSessionRegistry"/> for use by Browser and BrowserContext.
/// </summary>
internal sealed class BiDiSessionRegistry : IMotusSessionRegistry
{
    private readonly BiDiTransport _transport;
    private readonly ConcurrentDictionary<string, BiDiSession> _sessions = new();

    public IMotusSession BrowserSession { get; }

    internal BiDiSessionRegistry(BiDiTransport transport, string? browserSessionId = null)
    {
        _transport = transport;
        BrowserSession = new BiDiSession(transport, sessionId: browserSessionId);
    }

    public IMotusSession CreateSession(string sessionId)
    {
        var session = new BiDiSession(_transport, sessionId);
        _sessions[sessionId] = session;
        return session;
    }

    public bool TryGetSession(string sessionId, out IMotusSession? session)
    {
        var found = _sessions.TryGetValue(sessionId, out var bidiSession);
        session = bidiSession;
        return found;
    }

    public bool RemoveSession(string sessionId)
        => _sessions.TryRemove(sessionId, out _);

    internal IReadOnlyCollection<BiDiSession> ActiveSessions =>
        (IReadOnlyCollection<BiDiSession>)_sessions.Values;
}
