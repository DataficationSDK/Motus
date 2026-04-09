using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Performance;

[TestClass]
public class PerformanceMetricsCollectorBiDiTests
{
    [TestMethod]
    public void BiDiSession_DoesNotHavePerformanceMetricsCapability()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        Assert.AreEqual(
            MotusCapabilities.None,
            session.Capabilities & MotusCapabilities.PerformanceMetrics,
            "BiDi sessions should not have the PerformanceMetrics capability.");
    }

    [TestMethod]
    public void CdpSession_HasPerformanceMetricsCapability()
    {
        // Verify that AllCdp includes PerformanceMetrics
        Assert.AreNotEqual(
            MotusCapabilities.None,
            MotusCapabilities.AllCdp & MotusCapabilities.PerformanceMetrics,
            "AllCdp should include the PerformanceMetrics capability.");
    }
}
