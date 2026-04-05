using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Context;

[TestClass]
public class TracingCapabilityGuardTests
{
    [TestMethod]
    public async Task StartAsync_ThrowsNotSupportedException_OnBiDiTransport()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, sessionId: null);
        var tracing = new Tracing(session);

        var ex = await Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => tracing.StartAsync());

        StringAssert.Contains(ex.Message, "Tracing");
        StringAssert.Contains(ex.Message, "BiDi");
    }
}
