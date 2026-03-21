using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Transport.BiDi;

// Test-local types for BiDi session tests
internal sealed record BiDiTestNavigateParams(
    [property: JsonPropertyName("url")] string Url);

internal sealed record BiDiTestNavigateResult(
    [property: JsonPropertyName("frameId")] string FrameId,
    [property: JsonPropertyName("loaderId")] string? LoaderId);

[JsonSerializable(typeof(BiDiTestNavigateParams))]
[JsonSerializable(typeof(BiDiTestNavigateResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class BiDiTestJsonContext : JsonSerializerContext;

[TestClass]
public class BiDiSessionTests
{
    [TestMethod]
    public async Task SendAsync_Translates_PageNavigate_To_BiDi()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var session = new BiDiSession(transport, "ctx-1");

        // Queue BiDi success response for browsingContext.navigate
        socket.QueueResponse("""{"type":"success","id":1,"result":{"navigation":"nav-1","url":"https://example.com"}}""");

        var result = await session.SendAsync(
            "Page.navigate",
            new BiDiTestNavigateParams("https://example.com"),
            BiDiTestJsonContext.Default.BiDiTestNavigateParams,
            BiDiTestJsonContext.Default.BiDiTestNavigateResult,
            CancellationToken.None);

        Assert.AreEqual("https://example.com", result.FrameId);
        Assert.AreEqual("nav-1", result.LoaderId);

        // Verify the outbound message was a BiDi browsingContext.navigate
        var sentJson = socket.GetSentJson(0);
        using var doc = JsonDocument.Parse(sentJson);
        Assert.AreEqual("browsingContext.navigate", doc.RootElement.GetProperty("method").GetString());
    }

    [TestMethod]
    public async Task SendAsync_Unknown_Method_Throws_NotSupportedException()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var session = new BiDiSession(transport, "ctx-1");

        await Assert.ThrowsExceptionAsync<NotSupportedException>(async () =>
            await session.SendAsync(
                "SomeDomain.unknownMethod",
                new BiDiTestNavigateParams("test"),
                BiDiTestJsonContext.Default.BiDiTestNavigateParams,
                BiDiTestJsonContext.Default.BiDiTestNavigateResult,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SendAsync_BiDi_Error_Wraps_To_MotusProtocolException()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var session = new BiDiSession(transport, "ctx-1");

        socket.QueueResponse("""{"type":"error","id":1,"error":"unknown error","message":"Something failed"}""");

        var ex = await Assert.ThrowsExceptionAsync<MotusProtocolException>(async () =>
            await session.SendAsync(
                "Page.navigate",
                new BiDiTestNavigateParams("https://example.com"),
                BiDiTestJsonContext.Default.BiDiTestNavigateParams,
                BiDiTestJsonContext.Default.BiDiTestNavigateResult,
                CancellationToken.None));

        Assert.AreEqual("Page.navigate", ex.CommandSent);
        StringAssert.Contains(ex.Message, "Something failed");
    }

    [TestMethod]
    public async Task SendAsync_Disconnect_Wraps_To_MotusTargetClosedException()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var session = new BiDiSession(transport, "ctx-1");

        // Don't queue a response, just disconnect
        var sendTask = session.SendAsync(
            "Page.navigate",
            new BiDiTestNavigateParams("https://example.com"),
            BiDiTestJsonContext.Default.BiDiTestNavigateParams,
            BiDiTestJsonContext.Default.BiDiTestNavigateResult,
            CancellationToken.None);

        socket.SimulateDisconnect();

        var ex = await Assert.ThrowsExceptionAsync<MotusTargetClosedException>(
            async () => await sendTask);

        Assert.AreEqual("context", ex.TargetType);
        Assert.AreEqual("ctx-1", ex.TargetId);
    }

    [TestMethod]
    public async Task SubscribeAsync_Sends_SessionSubscribe_On_First_Call()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var session = new BiDiSession(transport, "ctx-1");

        // Queue a response for session.subscribe
        socket.QueueResponse("""{"type":"success","id":1,"result":{}}""");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Start MoveNextAsync first so the channel gets created, then inject event
        var enumerator = session.SubscribeAsync<JsonElement>(
            "Page.loadEventFired",
            BiDiJsonContext.Default.JsonElement,
            cts.Token).GetAsyncEnumerator(cts.Token);

        var moveTask = enumerator.MoveNextAsync();

        // Wait briefly for the subscribe to complete and channel to be created
        await Task.Delay(100);

        // Now inject the event
        socket.Enqueue("""{"type":"event","method":"browsingContext.load","params":{"context":"ctx-1","timestamp":1}}""");

        Assert.IsTrue(await moveTask);

        // Verify session.subscribe was sent
        var sentJson = socket.GetSentJson(0);
        using var doc = JsonDocument.Parse(sentJson);
        Assert.AreEqual("session.subscribe", doc.RootElement.GetProperty("method").GetString());

        await enumerator.DisposeAsync();
    }

    [TestMethod]
    public void CleanupChannels_Removes_Context_Channels()
    {
        var socket = new FakeCdpSocket();
        var transport = new BiDiTransport(socket);
        var session = new BiDiSession(transport, "ctx-1");

        var channel = transport.GetOrCreateEventChannel("browsingContext.load|ctx-1");
        session.CleanupChannels();

        Assert.IsTrue(channel.Reader.Completion.IsCompleted);
    }
}
