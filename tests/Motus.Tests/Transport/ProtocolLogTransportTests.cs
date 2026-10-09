using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Motus.Tests.Transport;

[TestClass]
public class ProtocolLogTransportTests
{
    private static JsonElement Params(string json) => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public async Task Off_SendsIdenticalBytes()
    {
        var plainSocket = new FakeCdpSocket();
        var loggedSocket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();

        await using var plain = new CdpTransport(plainSocket);
        await using var logged = new CdpTransport(loggedSocket, log: capture.Open());
        await plain.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);
        await logged.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        plainSocket.QueueResponse("""{"id":1,"result":{}}""");
        loggedSocket.QueueResponse("""{"id":1,"result":{}}""");
        await plain.SendRawAsync("Page.navigate", Params("""{"url":"https://example.com"}"""), "s1", CancellationToken.None);
        await logged.SendRawAsync("Page.navigate", Params("""{"url":"https://example.com"}"""), "s1", CancellationToken.None);

        CollectionAssert.AreEqual(plainSocket.SentMessages[0], loggedSocket.SentMessages[0]);
    }

    [TestMethod]
    public async Task Command_ThenResponse_InOrder_WithMethodAndElapsed()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        // Answered from inside SendAsync, before the send returns, which is the order a pipe can
        // produce. The command still has to come first in the log.
        socket.QueueResponse("""{"id":1,"result":{"frameId":"F1"}}""");
        await transport.SendRawAsync("Page.navigate", Params("""{"url":"https://example.com"}"""), null, CancellationToken.None);
        await transport.DisposeAsync();

        var lines = capture.Lines;
        Assert.AreEqual("open cdp-ws test:1", lines[0]);
        Assert.AreEqual("""-> #1 - Page.navigate {"url":"https://example.com"}""", lines[1]);
        StringAssert.Matches(lines[2], new Regex("""^<- #1 - Page\.navigate \d+\.\dms \{"frameId":"F1"\}$"""));
        Assert.AreEqual("closed by Motus", lines[3]);
        Assert.AreEqual(4, lines.Count, string.Join("\n", lines));
    }

    [TestMethod]
    public async Task Lines_CarryTimestampAndConnectionTag()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var log = capture.Open();
        var transport = new CdpTransport(socket, log: log);
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);
        await transport.DisposeAsync();

        foreach (var line in capture.RawLines)
            StringAssert.Matches(line, new Regex($@"^\d{{4}}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{{3}}Z {log.Tag} "));
    }

    [TestMethod]
    public async Task Event_IsLogged_EvenWithoutSubscriber()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        await using var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.Enqueue("""{"method":"Network.dataReceived","params":{"requestId":"7","dataLength":10},"sessionId":"SESSION-ABC"}""");

        var lines = await capture.WaitForAsync(l => l.StartsWith("<- event", StringComparison.Ordinal));
        Assert.IsTrue(lines.Contains("""<- event SESSION-ABC Network.dataReceived {"requestId":"7","dataLength":10}"""),
            string.Join("\n", lines));
    }

    [TestMethod]
    public async Task ErrorResponse_LogsCodeAndMessage()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.QueueResponse("""{"id":1,"error":{"code":-32000,"message":"No node with given id found"}}""");
        await Assert.ThrowsExceptionAsync<CdpProtocolException>(() =>
            transport.SendRawAsync("DOM.resolveNode", Params("""{"nodeId":5}"""), "S", CancellationToken.None));
        await transport.DisposeAsync();

        StringAssert.Matches(capture.Lines[2],
            new Regex("""^<- #1 S DOM\.resolveNode \d+\.\dms error -32000 "No node with given id found"$"""));
    }

    [TestMethod]
    public async Task CancelledCommand_LogsCancelledLine()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var send = transport.SendRawAsync("Runtime.evaluate", Params("""{"expression":"1"}"""), null, cts.Token);
        cts.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => send);
        await transport.DisposeAsync();

        Assert.IsTrue(capture.Lines.Any(l => Regex.IsMatch(l, @"^xx #1 - Runtime\.evaluate cancelled after \d+\.\dms$")),
            string.Join("\n", capture.Lines));
    }

    [TestMethod]
    public async Task ResponseWithNoPendingCommand_IsLogged()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        await using var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.EnqueueRaw("""{"id":99,"result":{"late":true}}""");

        var lines = await capture.WaitForAsync(l => l.StartsWith("<- #99", StringComparison.Ordinal));
        Assert.IsTrue(lines.Contains("""<- #99 - (no pending command) {"late":true}"""), string.Join("\n", lines));
    }

    [TestMethod]
    public async Task MalformedInbound_IsLogged()
    {
        var socket = new RawSocket();
        var capture = new ProtocolLogCapture();
        await using var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.Deliver("{not json");

        var lines = await capture.WaitForAsync(l => l.StartsWith("<- malformed", StringComparison.Ordinal));
        Assert.IsTrue(lines.Contains("<- malformed {not json"), string.Join("\n", lines));
    }

    [TestMethod]
    public async Task Disconnect_LogsClosedByBrowser()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.SimulateDisconnect();
        await capture.WaitForAsync(l => l.StartsWith("closed", StringComparison.Ordinal));
        await transport.DisposeAsync();

        var closed = capture.Lines.Where(l => l.StartsWith("closed", StringComparison.Ordinal)).ToArray();
        CollectionAssert.AreEqual(new[] { "closed by the browser" }, closed, "the close is logged once");
    }

    [TestMethod]
    public async Task SessionId_PrintedFull_DashForBrowserLevel()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.QueueResponse("""{"id":1,"result":{}}""");
        await transport.SendRawAsync("Target.getTargets", CdpTransport.EmptyJsonElement(), null, CancellationToken.None);
        socket.QueueResponse("""{"id":2,"result":{}}""");
        await transport.SendRawAsync("Page.enable", CdpTransport.EmptyJsonElement(), "0123456789ABCDEF0123456789ABCDEF", CancellationToken.None);
        await transport.DisposeAsync();

        CollectionAssert.Contains(capture.Lines.ToArray(), "-> #1 - Target.getTargets {}");
        CollectionAssert.Contains(capture.Lines.ToArray(), "-> #2 0123456789ABCDEF0123456789ABCDEF Page.enable {}");
    }

    [TestMethod]
    public async Task ConcurrentSends_EveryCommandPrecedesItsResponse()
    {
        const int count = 200;
        var socket = new ConcurrentFakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new CdpTransport(socket, log: capture.Open());
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        for (var i = 0; i < count; i++)
            socket.QueueResponse("""{"id":0,"result":{}}""");

        var sends = Enumerable.Range(0, count)
            .Select(_ => Task.Run(() => transport.SendRawAsync("Runtime.evaluate", CdpTransport.EmptyJsonElement(), null, CancellationToken.None)))
            .ToArray();
        await Task.WhenAll(sends);
        await transport.DisposeAsync();

        var lines = capture.Lines;
        for (var id = 1; id <= count; id++)
        {
            var sent = Indexes(lines, $"-> #{id} ");
            var answered = Indexes(lines, $"<- #{id} ");
            Assert.AreEqual(1, sent.Count, $"#{id} sent once");
            Assert.AreEqual(1, answered.Count, $"#{id} answered once");
            Assert.IsTrue(sent[0] < answered[0], $"#{id} is logged before its response");
        }
    }

    [TestMethod]
    public async Task SinkCallbackThrows_TransportUnaffected()
    {
        var socket = new FakeCdpSocket();
        var warnings = new StringWriter();
        var log = ProtocolLog.Open(
            new Motus.Abstractions.ProtocolLogOptions { Sink = _ => throw new InvalidOperationException("sink broke") },
            "cdp-ws", "test:1", warnings)!;
        var transport = new CdpTransport(socket, log: log);
        await transport.ConnectAsync(new Uri("ws://127.0.0.1:9222"), CancellationToken.None);

        socket.QueueResponse("""{"id":1,"result":{"ok":true}}""");
        var result = await transport.SendRawAsync("Runtime.evaluate", CdpTransport.EmptyJsonElement(), null, CancellationToken.None);
        socket.QueueResponse("""{"id":2,"result":{"ok":true}}""");
        await transport.SendRawAsync("Runtime.evaluate", CdpTransport.EmptyJsonElement(), null, CancellationToken.None);
        await transport.DisposeAsync();

        Assert.IsTrue(result.GetProperty("ok").GetBoolean());
        var reported = warnings.ToString();
        StringAssert.Contains(reported, "a protocol log sink threw (sink broke)");
        Assert.AreEqual(1, Regex.Matches(reported, "sink threw").Count, "reported once");
    }

    [TestMethod]
    public async Task BiDi_LogsCommandResponseEventAndError()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new BiDiTransport(socket, ProtocolLog.Open(capture.Options, "bidi-ws", "test:1"));
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        socket.QueueResponse("""{"type":"success","id":1,"result":{"ready":true}}""");
        await transport.SendRawAsync("session.status", BiDiTransport.EmptyJsonElement(), CancellationToken.None);

        socket.QueueResponse("""{"type":"error","id":2,"error":"no such frame","message":"Frame gone"}""");
        await Assert.ThrowsExceptionAsync<BiDiProtocolException>(() =>
            transport.SendRawAsync("browsingContext.navigate", Params("""{"context":"ctx","url":"about:blank"}"""), CancellationToken.None));

        socket.Enqueue("""{"type":"event","method":"network.beforeRequestSent","params":{"context":"ctx","request":{"headers":[{"name":"Cookie","value":{"type":"string","value":"SECRET"}}]}}}""");
        await capture.WaitForAsync(l => l.StartsWith("<- event", StringComparison.Ordinal));
        await transport.DisposeAsync();

        var lines = capture.Lines;
        Assert.AreEqual("open bidi-ws test:1", lines[0]);
        Assert.AreEqual("-> #1 - session.status {}", lines[1]);
        StringAssert.Matches(lines[2], new Regex("""^<- #1 - session\.status \d+\.\dms \{"ready":true\}$"""));
        Assert.AreEqual("""-> #2 - browsingContext.navigate {"context":"ctx","url":"about:blank"}""", lines[3]);
        StringAssert.Matches(lines[4], new Regex("""^<- #2 - browsingContext\.navigate \d+\.\dms error no such frame "Frame gone"$"""));
        Assert.AreEqual(
            """<- event - network.beforeRequestSent {"context":"ctx","request":{"headers":[{"name":"Cookie","value":"<redacted>"}]}}""",
            lines[5]);
        Assert.AreEqual("closed by Motus", lines[6]);
    }

    [TestMethod]
    public async Task BiDi_Disconnect_LogsClosedByBrowser()
    {
        var socket = new FakeCdpSocket();
        var capture = new ProtocolLogCapture();
        var transport = new BiDiTransport(socket, ProtocolLog.Open(capture.Options, "bidi-ws", "test:1"));
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        socket.SimulateDisconnect();
        await capture.WaitForAsync(l => l == "closed by the browser");
        await transport.DisposeAsync();
    }

    private static List<int> Indexes(IReadOnlyList<string> lines, string prefix)
    {
        var found = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith(prefix, StringComparison.Ordinal))
                found.Add(i);
        }
        return found;
    }

    /// <summary>
    /// A socket that delivers exactly the bytes it is handed, including ones that are not JSON.
    /// </summary>
    private sealed class RawSocket : ICdpSocket
    {
        private readonly Channel<byte[]> _inbox = Channel.CreateUnbounded<byte[]>();

        public bool IsOpen => true;

        public Task ConnectAsync(Uri endpointUri, CancellationToken ct) => Task.CompletedTask;

        public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken ct) => Task.CompletedTask;

        public async ValueTask<ReadOnlyMemory<byte>> ReceiveAsync(CancellationToken ct)
        {
            try
            {
                return await _inbox.Reader.ReadAsync(ct);
            }
            catch (ChannelClosedException)
            {
                return ReadOnlyMemory<byte>.Empty;
            }
        }

        internal void Deliver(string text) => _inbox.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

        public ValueTask DisposeAsync()
        {
            _inbox.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
