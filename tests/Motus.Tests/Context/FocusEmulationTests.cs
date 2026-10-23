using System.Text;
using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Context;

/// <summary>
/// Pins that a page Motus opens is told to behave as though its window had focus, so
/// <c>document.hasFocus()</c> holds in every page of a run with several contexts, and that a
/// browser which cannot do that still opens the page.
/// </summary>
[TestClass]
public class FocusEmulationTests
{
    private const string FocusEmulation = "Emulation.setFocusEmulationEnabled";

    private MethodAwareFakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new MethodAwareFakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
        _browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        _socket.Respond(
            "Browser.getVersion",
            """{"result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");

        await _browser.InitializeAsync(CancellationToken.None);

        _socket.Respond("Target.createBrowserContext", """{"result": {"browserContextId": "ctx-1"}}""");
        _socket.Respond("Target.createTarget", """{"result": {"targetId": "target-1"}}""");
        _socket.Respond("Target.attachToTarget", """{"result": {"sessionId": "session-1"}}""");
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    [TestMethod]
    public async Task NewPageAsync_EmulatesFocusOnThePagesOwnSession()
    {
        await _browser.NewPageAsync();

        var sent = Sent().Where(m => m.Method == FocusEmulation).ToList();

        Assert.AreEqual(1, sent.Count, "focus is emulated once per page");
        Assert.AreEqual("session-1", sent[0].SessionId, "focus is emulated on the page, not the browser");
        Assert.IsTrue(sent[0].Params.GetProperty("enabled").GetBoolean());
    }

    [TestMethod]
    public async Task NewPageAsync_StillOpensThePage_WhenTheBrowserDoesNotKnowTheCommand()
    {
        _socket.Respond(
            FocusEmulation,
            $$"""{"error": {"code": -32601, "message": "'{{FocusEmulation}}' wasn't found"}, "sessionId": "session-1"}""");

        var page = await _browser.NewPageAsync();

        Assert.IsNotNull(page);
        Assert.IsFalse(page.IsClosed);
        Assert.AreEqual(1, _browser.Contexts[0].Pages.Count);
    }

    [TestMethod]
    public async Task NewPageAsync_OnAnAdoptedContext_LeavesFocusAlone()
    {
        // A context that was already open in the browser belongs to whoever is using it.
        var adopted = new BrowserContext(_browser, _registry, "ctx-user", options: null, adopted: true);

        await adopted.NewPageAsync();

        Assert.IsFalse(Sent().Any(m => m.Method == FocusEmulation));
    }

    private List<(string Method, string? SessionId, JsonElement Params)> Sent()
        => _socket.SentMessages
            .Select(bytes =>
            {
                var root = JsonDocument.Parse(Encoding.UTF8.GetString(bytes)).RootElement;
                return (
                    root.GetProperty("method").GetString()!,
                    root.TryGetProperty("sessionId", out var s) ? s.GetString() : null,
                    root.TryGetProperty("params", out var p) ? p.Clone() : default);
            })
            .ToList();
}
