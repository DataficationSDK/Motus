namespace Motus.Tests.Transport;

[TestClass]
public class CdpSessionRegistryTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public void BrowserSession_HasNullSessionId()
    {
        Assert.IsNull(_registry.BrowserSession.SessionId);
    }

    [TestMethod]
    public void CreateSession_ReturnsSessionWithCorrectId()
    {
        var session = _registry.CreateSession("session-abc");

        Assert.AreEqual("session-abc", session.SessionId);
    }

    [TestMethod]
    public void TryGetSession_ReturnsTrueForExistingSession()
    {
        _registry.CreateSession("session-abc");

        Assert.IsTrue(_registry.TryGetSession("session-abc", out var session));
        Assert.IsNotNull(session);
        Assert.AreEqual("session-abc", session.SessionId);
    }

    [TestMethod]
    public void TryGetSession_ReturnsFalseForUnknownSession()
    {
        Assert.IsFalse(_registry.TryGetSession("nonexistent", out _));
    }

    [TestMethod]
    public void RemoveSession_RemovesExistingSession()
    {
        _registry.CreateSession("session-abc");

        Assert.IsTrue(_registry.RemoveSession("session-abc"));
        Assert.IsFalse(_registry.TryGetSession("session-abc", out _));
    }

    [TestMethod]
    public void RemoveSession_ReturnsFalseForUnknownSession()
    {
        Assert.IsFalse(_registry.RemoveSession("nonexistent"));
    }

    [TestMethod]
    public void ActiveSessions_ReflectsCurrentState()
    {
        Assert.AreEqual(0, _registry.ActiveSessions.Count);

        _registry.CreateSession("s1");
        _registry.CreateSession("s2");

        Assert.AreEqual(2, _registry.ActiveSessions.Count);

        _registry.RemoveSession("s1");

        Assert.AreEqual(1, _registry.ActiveSessions.Count);
    }

    [TestMethod]
    public void CreateSession_OverwritesExistingSessionWithSameId()
    {
        var session1 = _registry.CreateSession("s1");
        var session2 = _registry.CreateSession("s1");

        // Should return a new session instance
        Assert.AreNotSame(session1, session2);

        Assert.IsTrue(_registry.TryGetSession("s1", out var retrieved));
        Assert.AreSame(session2, retrieved);
    }
}
