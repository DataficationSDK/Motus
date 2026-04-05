namespace Motus.Tests.Transport;

[TestClass]
public class TransportAbstractionTests
{
    [TestMethod]
    public void CdpSession_Implements_IMotusSession()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);
        var session = new CdpSession(transport, "test-session");

        Assert.IsInstanceOfType<IMotusSession>(session);
    }

    [TestMethod]
    public void CdpSessionRegistry_Implements_IMotusSessionRegistry()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);
        var registry = new CdpSessionRegistry(transport);

        Assert.IsInstanceOfType<IMotusSessionRegistry>(registry);
    }

    [TestMethod]
    public void CdpTransport_Implements_IMotusTransport()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);

        Assert.IsInstanceOfType<IMotusTransport>(transport);
    }

    [TestMethod]
    public void CdpTransport_Capabilities_Returns_AllCdp()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);

        Assert.AreEqual(MotusCapabilities.AllCdp, transport.Capabilities);
    }

    [TestMethod]
    public void CapabilityGuard_Require_Passes_When_Capability_Present()
    {
        var caps = MotusCapabilities.AllCdp;
        CapabilityGuard.Require(caps, MotusCapabilities.FetchInterception, "Fetch");
    }

    [TestMethod]
    public void CapabilityGuard_Require_Throws_When_Capability_Missing()
    {
        var caps = MotusCapabilities.None;

        var ex = Assert.ThrowsException<NotSupportedException>(
            () => CapabilityGuard.Require(caps, MotusCapabilities.Tracing, "Tracing"));

        StringAssert.Contains(ex.Message, "Tracing");
    }

    [TestMethod]
    public void SessionRegistry_Returns_IMotusSession_Types()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);
        var registry = new CdpSessionRegistry(transport);

        IMotusSession browserSession = registry.BrowserSession;
        Assert.IsNull(browserSession.SessionId);

        IMotusSession pageSession = registry.CreateSession("page-1");
        Assert.AreEqual("page-1", pageSession.SessionId);

        Assert.IsTrue(registry.TryGetSession("page-1", out var found));
        Assert.AreSame(pageSession, found);
    }

    [TestMethod]
    public void CdpSession_Capabilities_Returns_AllCdp()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);
        var session = new CdpSession(transport, "test");

        Assert.AreEqual(MotusCapabilities.AllCdp, session.Capabilities);
    }

    [TestMethod]
    public void AllCdp_Includes_SecurityOverrides()
    {
        Assert.AreNotEqual(MotusCapabilities.None,
            MotusCapabilities.AllCdp & MotusCapabilities.SecurityOverrides);
    }

    [TestMethod]
    public void AllCdp_Includes_AccessibilityTree()
    {
        Assert.AreNotEqual(MotusCapabilities.None,
            MotusCapabilities.AllCdp & MotusCapabilities.AccessibilityTree);
    }

    [TestMethod]
    public void CapabilityGuard_GetTransportDescription_CdpSession()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);
        var session = new CdpSession(transport, "test");

        var desc = CapabilityGuard.GetTransportDescription(session);
        StringAssert.Contains(desc, "CDP");
    }

    [TestMethod]
    public void CdpSession_CleanupChannels_Does_Not_Throw_For_Null_SessionId()
    {
        var socket = new FakeCdpSocket();
        var transport = new CdpTransport(socket);
        var session = new CdpSession(transport, sessionId: null);

        // Should be a no-op, not throw
        session.CleanupChannels();
    }
}
