using Motus.Selectors;
using Motus.Tests.Transport;

namespace Motus.Tests.Selectors;

[TestClass]
public class DomFingerprintBuilderTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSession _session = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);
        _session = new CdpSession(_transport, sessionId: null);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task TryBuildAsync_ReturnsFingerprint_WithExpectedFields()
    {
        // DOM.describeNode on the target node: returns tag, parent id, attributes, backendNodeId.
        _socket.QueueResponse("""
            {"id":1,"result":{"node":{"localName":"button","nodeName":"BUTTON","nodeId":42,"parentId":40,"backendNodeId":501,"attributes":["id","submit","role","button","data-testid","login-submit","class","btn primary"]}}}
            """);
        // DOM.getOuterHTML for visible text extraction
        _socket.QueueResponse("""
            {"id":2,"result":{"outerHTML":"<button id='submit' role='button'>Sign in</button>"}}
            """);
        // Ancestor walk: describeNode(parentId=40) -> form with parent 30
        _socket.QueueResponse("""
            {"id":3,"result":{"node":{"localName":"form","nodeId":40,"parentId":30}}}
            """);
        // describeNode(parentId=30) -> section with parent 20
        _socket.QueueResponse("""
            {"id":4,"result":{"node":{"localName":"section","nodeId":30,"parentId":20}}}
            """);
        // describeNode(parentId=20) -> div (root of walk; no further parent needed)
        _socket.QueueResponse("""
            {"id":5,"result":{"node":{"localName":"div","nodeId":20,"parentId":0}}}
            """);

        var fingerprint = await DomFingerprintBuilder.TryBuildAsync(_session, backendNodeId: 501, CancellationToken.None);

        Assert.IsNotNull(fingerprint);
        Assert.AreEqual("button", fingerprint.TagName);
        Assert.AreEqual("submit", fingerprint.KeyAttributes["id"]);
        Assert.AreEqual("button", fingerprint.KeyAttributes["role"]);
        Assert.AreEqual("login-submit", fingerprint.KeyAttributes["data-testid"]);
        Assert.IsFalse(fingerprint.KeyAttributes.ContainsKey("class"), "class is not a key attribute");
        Assert.AreEqual("Sign in", fingerprint.VisibleText);
        Assert.AreEqual("div > section > form", fingerprint.AncestorPath);
        Assert.AreEqual(64, fingerprint.Hash.Length);
    }

    [TestMethod]
    public async Task TryBuildAsync_DescribeNodeFails_ReturnsNull()
    {
        _socket.QueueResponse("""{"id":1,"error":{"code":-32000,"message":"Could not find node"}}""");

        var fingerprint = await DomFingerprintBuilder.TryBuildAsync(_session, backendNodeId: 999, CancellationToken.None);

        Assert.IsNull(fingerprint);
    }

    [TestMethod]
    public async Task TryBuildAsync_NoParent_ProducesEmptyAncestorPath()
    {
        _socket.QueueResponse("""
            {"id":1,"result":{"node":{"localName":"html","nodeId":1,"backendNodeId":1,"attributes":[]}}}
            """);
        _socket.QueueResponse("""
            {"id":2,"result":{"outerHTML":"<html></html>"}}
            """);

        var fingerprint = await DomFingerprintBuilder.TryBuildAsync(_session, backendNodeId: 1, CancellationToken.None);

        Assert.IsNotNull(fingerprint);
        Assert.AreEqual("html", fingerprint.TagName);
        Assert.AreEqual(string.Empty, fingerprint.AncestorPath);
        Assert.AreEqual(0, fingerprint.KeyAttributes.Count);
    }

    [TestMethod]
    public async Task TryBuildAsync_LowercasesTagName()
    {
        _socket.QueueResponse("""
            {"id":1,"result":{"node":{"localName":"BUTTON","nodeId":1,"backendNodeId":1,"attributes":[]}}}
            """);
        _socket.QueueResponse("""
            {"id":2,"result":{"outerHTML":"<BUTTON></BUTTON>"}}
            """);

        var fingerprint = await DomFingerprintBuilder.TryBuildAsync(_session, backendNodeId: 1, CancellationToken.None);

        Assert.IsNotNull(fingerprint);
        Assert.AreEqual("button", fingerprint.TagName);
    }
}
