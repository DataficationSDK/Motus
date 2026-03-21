using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Transport.BiDi;

[TestClass]
public class BiDiTransportTests
{
    [TestMethod]
    public async Task SendRawAsync_Correlates_Success_Response()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        // Queue a BiDi success response for id 1
        socket.QueueResponse("""{"type":"success","id":1,"result":{"ready":true,"message":"OK"}}""");

        var result = await transport.SendRawAsync(
            "session.status", BiDiTransport.EmptyJsonElement(), CancellationToken.None);

        Assert.AreEqual(JsonValueKind.Object, result.ValueKind);
        Assert.IsTrue(result.GetProperty("ready").GetBoolean());
        Assert.AreEqual("OK", result.GetProperty("message").GetString());
    }

    [TestMethod]
    public async Task SendRawAsync_Throws_On_Error_Response()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        socket.QueueResponse("""{"type":"error","id":1,"error":"invalid argument","message":"Bad params"}""");

        var ex = await Assert.ThrowsExceptionAsync<BiDiProtocolException>(async () =>
            await transport.SendRawAsync(
                "session.status", BiDiTransport.EmptyJsonElement(), CancellationToken.None));

        Assert.AreEqual("invalid argument", ex.ErrorCode);
        Assert.AreEqual("Bad params", ex.Message);
    }

    [TestMethod]
    public async Task DispatchEvent_Routes_To_Correct_Channel()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        var channel = transport.GetOrCreateEventChannel("browsingContext.load|ctx-1");

        // Inject a BiDi event
        socket.Enqueue("""{"type":"event","method":"browsingContext.load","params":{"context":"ctx-1","timestamp":12345}}""");

        // Wait for dispatch
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var raw = await channel.Reader.ReadAsync(cts.Token);

        Assert.AreEqual("ctx-1", raw.ContextId);
        Assert.AreEqual(12345, raw.Params.GetProperty("timestamp").GetInt32());
    }

    [TestMethod]
    public async Task DispatchEvent_Routes_To_Wildcard_Channel()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        // Subscribe with empty context (browser-level wildcard)
        var channel = transport.GetOrCreateEventChannel("browsingContext.load|");

        socket.Enqueue("""{"type":"event","method":"browsingContext.load","params":{"context":"ctx-1","timestamp":1}}""");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var raw = await channel.Reader.ReadAsync(cts.Token);

        Assert.AreEqual("ctx-1", raw.ContextId);
    }

    [TestMethod]
    public async Task Disconnect_Fires_Event_And_Faults_Pending()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        bool disconnectedFired = false;
        transport.Disconnected += _ => disconnectedFired = true;

        // Start a command that will not get a response
        var sendTask = transport.SendRawAsync(
            "session.status", BiDiTransport.EmptyJsonElement(), CancellationToken.None);

        // Simulate disconnect
        socket.SimulateDisconnect();

        await Assert.ThrowsExceptionAsync<BiDiDisconnectedException>(async () =>
            await sendTask);

        // Give the receive loop a moment to fire
        await Task.Delay(50);
        Assert.IsTrue(disconnectedFired);
    }

    [TestMethod]
    public async Task RemoveChannelsForContext_Completes_Channels()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);

        var channel = transport.GetOrCreateEventChannel("browsingContext.load|ctx-1");
        transport.RemoveChannelsForContext("ctx-1");

        // Channel should be completed
        Assert.IsTrue(channel.Reader.Completion.IsCompleted);
    }

    [TestMethod]
    public async Task DisposeAsync_Faults_Pending_And_Completes_Channels()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);

        var channel = transport.GetOrCreateEventChannel("test.event|");

        var sendTask = transport.SendRawAsync(
            "session.status", BiDiTransport.EmptyJsonElement(), CancellationToken.None);

        await transport.DisposeAsync();

        // Allow continuations to propagate
        try { await sendTask; }
        catch { /* Expected: faulted or canceled */ }

        Assert.IsTrue(sendTask.IsFaulted || sendTask.IsCanceled,
            $"Expected faulted or canceled, got {sendTask.Status}");

        // Channel should be completed
        Assert.IsTrue(channel.Reader.Completion.IsCompleted);
    }
}
