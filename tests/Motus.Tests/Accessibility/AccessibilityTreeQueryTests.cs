using Motus.Tests.Transport;

namespace Motus.Tests.Accessibility;

[TestClass]
public class AccessibilityTreeQueryTests
{
    [TestMethod]
    public async Task GetTreeAsync_BiDiTransport_ReturnsEmptyTreeWithDiagnostic()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        var query = new AccessibilityTreeQuery(session);
        var result = await query.GetTreeAsync(CancellationToken.None);

        Assert.AreEqual(0, result.AllWalkableNodes.Count);
        Assert.AreEqual(0, result.Roots.Count);
        Assert.AreEqual(0, result.IgnoredCount);
        Assert.IsNotNull(result.DiagnosticMessage);
        Assert.IsTrue(result.DiagnosticMessage.Contains("not supported"));
    }
}
