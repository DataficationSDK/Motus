using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Handles;

[TestClass]
public class JsHandleTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSession _session = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(_transport);
        _session = registry.CreateSession("test-session");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task EvaluateAsync_ReturnsDeserializedValue()
    {
        var handle = new JsHandle(_session, "obj-1");

        var evalTask = handle.EvaluateAsync<int>("function() { return this.length; }");

        _socket.Enqueue("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "number", "value": 42 }
                }
            }
            """);

        var result = await evalTask;
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task GetPropertyAsync_ReturnsNewHandle()
    {
        var handle = new JsHandle(_session, "obj-1");

        var propTask = handle.GetPropertyAsync("name");

        _socket.Enqueue("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "object", "objectId": "obj-2" }
                }
            }
            """);

        var propHandle = await propTask;
        Assert.IsNotNull(propHandle);
    }

    [TestMethod]
    public async Task JsonValueAsync_ReturnsDeserializedValue()
    {
        var handle = new JsHandle(_session, "obj-1");

        var valueTask = handle.JsonValueAsync<string>();

        _socket.Enqueue("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "string", "value": "hello" }
                }
            }
            """);

        var value = await valueTask;
        Assert.AreEqual("hello", value);
    }

    [TestMethod]
    public async Task DisposeAsync_SendsReleaseObject()
    {
        var handle = new JsHandle(_session, "obj-1");

        var disposeTask = handle.DisposeAsync();
        _socket.Enqueue("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await disposeTask;

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Runtime.releaseObject"));
        Assert.IsTrue(sent.Contains("obj-1"));
    }

    [TestMethod]
    public async Task EvaluateAsync_ThrowsOnException()
    {
        var handle = new JsHandle(_session, "obj-1");

        var evalTask = handle.EvaluateAsync<int>("function() { throw new Error('fail'); }");

        _socket.Enqueue("""
            {
                "id": 1,
                "sessionId": "test-session",
                "result": {
                    "result": { "type": "undefined" },
                    "exceptionDetails": {
                        "exceptionId": 1,
                        "text": "Error: fail",
                        "lineNumber": 0,
                        "columnNumber": 0
                    }
                }
            }
            """);

        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => evalTask);
    }
}
