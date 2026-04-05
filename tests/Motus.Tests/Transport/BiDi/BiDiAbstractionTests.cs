using Motus.Tests.Transport;

namespace Motus.Tests.Transport.BiDi;

[TestClass]
public class BiDiAbstractionTests
{
    [TestMethod]
    public void BiDiSession_Implements_IMotusSession()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        Assert.IsInstanceOfType<IMotusSession>(session);
    }

    [TestMethod]
    public void BiDiSessionRegistry_Implements_IMotusSessionRegistry()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var registry = new BiDiSessionRegistry(transport);

        Assert.IsInstanceOfType<IMotusSessionRegistry>(registry);
    }

    [TestMethod]
    public void BiDiTransport_Implements_IMotusTransport()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);

        Assert.IsInstanceOfType<IMotusTransport>(transport);
    }

    [TestMethod]
    public void BiDiTransport_Capabilities_Returns_AllBiDi()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);

        Assert.AreEqual(MotusCapabilities.AllBiDi, transport.Capabilities);
    }

    [TestMethod]
    public void CapabilityGuard_Throws_For_CdpOnly_On_BiDi_Transport()
    {
        var caps = MotusCapabilities.AllBiDi;

        var ex = Assert.ThrowsException<NotSupportedException>(
            () => CapabilityGuard.Require(caps, MotusCapabilities.Tracing, "Tracing"));

        StringAssert.Contains(ex.Message, "Tracing");
    }

    [TestMethod]
    public void CapabilityGuard_Passes_For_BiDi_Capabilities()
    {
        var caps = MotusCapabilities.AllBiDi;
        CapabilityGuard.Require(caps, MotusCapabilities.BiDiNetworkIntercept, "Network");
    }

    [TestMethod]
    public void SessionRegistry_Returns_IMotusSession_Types()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var registry = new BiDiSessionRegistry(transport);

        IMotusSession browserSession = registry.BrowserSession;
        Assert.IsNull(browserSession.SessionId);

        IMotusSession pageSession = registry.CreateSession("ctx-1");
        Assert.AreEqual("ctx-1", pageSession.SessionId);

        Assert.IsTrue(registry.TryGetSession("ctx-1", out var found));
        Assert.AreSame(pageSession, found);
    }

    [TestMethod]
    public void BiDiSession_Capabilities_Returns_AllBiDi()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        Assert.AreEqual(MotusCapabilities.AllBiDi, session.Capabilities);
    }

    [TestMethod]
    public void AllBiDi_Excludes_SecurityOverrides()
    {
        Assert.AreEqual(MotusCapabilities.None,
            MotusCapabilities.AllBiDi & MotusCapabilities.SecurityOverrides);
    }

    [TestMethod]
    public void AllBiDi_Excludes_AccessibilityTree()
    {
        Assert.AreEqual(MotusCapabilities.None,
            MotusCapabilities.AllBiDi & MotusCapabilities.AccessibilityTree);
    }

    [TestMethod]
    public void CapabilityGuard_GetTransportDescription_BiDiSession()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        var desc = CapabilityGuard.GetTransportDescription(session);
        StringAssert.Contains(desc, "BiDi");
    }

    [TestMethod]
    public void BiDiSession_CleanupChannels_Does_Not_Throw_For_Null_SessionId()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, sessionId: null);

        session.CleanupChannels();
    }
}
