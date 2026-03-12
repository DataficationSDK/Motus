using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Browser;

[TestClass]
public class BrowserTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    [TestMethod]
    public async Task InitializeAsync_SetsVersionFromGetVersionResponse()
    {
        var browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = browser.InitializeAsync(CancellationToken.None);

        // Enqueue the response for Browser.getVersion (id will be 1)
        _socket.Enqueue("""
            {
                "id": 1,
                "result": {
                    "protocolVersion": "1.3",
                    "product": "Chrome/120.0.6099.0",
                    "revision": "@abc123",
                    "userAgent": "Mozilla/5.0",
                    "jsVersion": "12.0.267"
                }
            }
            """);

        await initTask;

        Assert.AreEqual("Chrome/120.0.6099.0", browser.Version);
        Assert.IsTrue(browser.IsConnected);
    }

    [TestMethod]
    public async Task IsConnected_BecomesFalse_OnTransportDisconnect()
    {
        var browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;

        Assert.IsTrue(browser.IsConnected);

        _socket.SimulateDisconnect();
        // Allow the receive loop to process the disconnect
        await Task.Delay(50);

        Assert.IsFalse(browser.IsConnected);
    }

    [TestMethod]
    public async Task Disconnected_EventFires_OnTransportDisconnect()
    {
        var browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;

        var disconnectedFired = false;
        browser.Disconnected += (_, _) => disconnectedFired = true;

        _socket.SimulateDisconnect();
        await Task.Delay(50);

        Assert.IsTrue(disconnectedFired);
    }

    [TestMethod]
    public async Task Contexts_ReturnsEmptyList()
    {
        var browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;

        Assert.AreEqual(0, browser.Contexts.Count);
    }

    [TestMethod]
    public async Task SlowMo_AddsDelayToCommands()
    {
        await _transport.DisposeAsync();

        _socket = new FakeCdpSocket();
        var slowTransport = new CdpTransport(_socket, TimeSpan.FromMilliseconds(200));
        await slowTransport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(slowTransport);

        var browser = new Motus.Browser(
            slowTransport, registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var initTask = browser.InitializeAsync(CancellationToken.None);

        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
        sw.Stop();

        Assert.IsTrue(sw.ElapsedMilliseconds >= 150,
            $"SlowMo should add delay, but elapsed was {sw.ElapsedMilliseconds}ms");

        await slowTransport.DisposeAsync();
    }
}
