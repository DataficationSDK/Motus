using System.Text.Json;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Dialog;

[TestClass]
public class DialogTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private IMotusSession _session = null!;

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
    public void Dialog_Properties_AreSetCorrectly()
    {
        var dialog = new Motus.Dialog(_session, DialogType.Prompt, "Enter name:", "default");

        Assert.AreEqual(DialogType.Prompt, dialog.Type);
        Assert.AreEqual("Enter name:", dialog.Message);
        Assert.AreEqual("default", dialog.DefaultValue);
    }

    [TestMethod]
    public async Task AcceptAsync_SendsHandleDialogCommand()
    {
        var dialog = new Motus.Dialog(_session, DialogType.Alert, "Hello", null);

        var acceptTask = dialog.AcceptAsync();
        _socket.Enqueue("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await acceptTask;

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Page.handleJavaScriptDialog"));
        Assert.IsTrue(sent.Contains("\"accept\":true"));
    }

    [TestMethod]
    public async Task DismissAsync_SendsHandleDialogWithFalse()
    {
        var dialog = new Motus.Dialog(_session, DialogType.Confirm, "OK?", null);

        var dismissTask = dialog.DismissAsync();
        _socket.Enqueue("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await dismissTask;

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Page.handleJavaScriptDialog"));
        Assert.IsTrue(sent.Contains("\"accept\":false"));
    }

    [TestMethod]
    public async Task AcceptAsync_WithPromptText_IncludesText()
    {
        var dialog = new Motus.Dialog(_session, DialogType.Prompt, "Name?", "");

        var acceptTask = dialog.AcceptAsync("Claude");
        _socket.Enqueue("""{"id": 1, "sessionId": "test-session", "result": {}}""");
        await acceptTask;

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("\"promptText\":\"Claude\""));
    }
}
