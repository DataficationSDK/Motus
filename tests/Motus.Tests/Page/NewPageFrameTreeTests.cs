using System.Text;
using System.Text.Json;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

/// <summary>
/// What a page from <c>NewPageAsync</c> knows about its frames before anything has navigated it.
/// The browser never announces a new tab's initial about:blank, so the page reads its frame tree
/// while it initializes instead of waiting for a navigation that may never come.
/// </summary>
[TestClass]
public class NewPageFrameTreeTests
{
    private const string InitialFrameTree = """
        {
            "sessionId": "session-1",
            "result": {
                "frameTree": {
                    "frame": {
                        "id": "target-1",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "about:blank"
                    }
                }
            }
        }
        """;

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
    public async Task NewPageAsync_SeedsMainFrameFromFrameTree()
    {
        _socket.Respond("Page.getFrameTree", InitialFrameTree);

        var page = await _browser.NewPageAsync();

        Assert.AreEqual("about:blank", page.Url);
        Assert.AreEqual("about:blank", page.MainFrame.Url);
        Assert.AreEqual(1, page.Frames.Count, "the page holds its main frame and nothing else");
        Assert.AreSame(page.MainFrame, page.Frames[0]);
    }

    [TestMethod]
    public async Task NewPageAsync_ReadsFrameTreeDuringInitialization()
    {
        _socket.Respond("Page.getFrameTree", InitialFrameTree);

        await _browser.NewPageAsync();

        var sent = SentMethods();
        var treeReads = sent.Where(m => m.Method == "Page.getFrameTree").ToList();

        Assert.AreEqual(1, treeReads.Count, "the tree is read exactly once");
        Assert.AreEqual("session-1", treeReads[0].SessionId, "the tree is read on the page's own session");
        Assert.IsTrue(
            sent.FindIndex(m => m.Method == "Page.getFrameTree") > sent.FindIndex(m => m.Method == "Runtime.enable"),
            "the tree is read after the domains are enabled, so nothing that changes after it is missed");
    }

    [TestMethod]
    public async Task NewPageAsync_EvaluateAsync_WorksBeforeAnyNavigation()
    {
        _socket.Respond("Page.getFrameTree", InitialFrameTree);
        _socket.SetHandler(envelope => envelope.GetProperty("method").GetString() switch
        {
            "Runtime.evaluate" => NumberResult(envelope.GetProperty("id").GetInt32(), 2),
            var method => throw new InvalidOperationException($"Unexpected {method}"),
        });

        var page = await _browser.NewPageAsync();
        DeliverMainWorldContext(contextId: 7, frameId: "target-1");
        await Task.Delay(100);

        var result = await page.EvaluateAsync<int>("1 + 1");

        Assert.AreEqual(2, result);
        using var sent = JsonDocument.Parse(LastSent("Runtime.evaluate"));
        Assert.AreEqual(
            7,
            sent.RootElement.GetProperty("params").GetProperty("contextId").GetInt32(),
            "the evaluation runs in the main world the browser reported for the seeded frame");
    }

    [TestMethod]
    public async Task NewPageAsync_SetContentAsync_WorksBeforeAnyNavigation()
    {
        _socket.Respond("Page.getFrameTree", InitialFrameTree);
        _socket.SetHandler(envelope => envelope.GetProperty("method").GetString() switch
        {
            "Runtime.evaluate" => UndefinedResult(envelope.GetProperty("id").GetInt32()),
            var method => throw new InvalidOperationException($"Unexpected {method}"),
        });

        var page = await _browser.NewPageAsync();

        await page.SetContentAsync("<p>hi</p>");

        using var sent = JsonDocument.Parse(LastSent("Runtime.evaluate"));
        StringAssert.Contains(
            sent.RootElement.GetProperty("params").GetProperty("expression").GetString(),
            "document.write",
            StringComparison.Ordinal);
    }

    [TestMethod]
    public async Task NewPageAsync_EmptyFrameTree_LeavesMainFrameToFirstNavigation()
    {
        // Nothing registered for Page.getFrameTree, so the fake answers with an empty result.
        var page = await _browser.NewPageAsync();

        Assert.AreEqual(string.Empty, page.Url);
        Assert.AreEqual(0, page.Frames.Count);

        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "https://example.test/"
                    }
                }
            }
            """);
        await Task.Delay(100);

        Assert.AreEqual("https://example.test/", page.Url);
        Assert.AreEqual("https://example.test/", page.MainFrame.Url);
    }

    [TestMethod]
    public async Task FrameNavigated_ChildFrameFirst_DoesNotClaimMainFrame()
    {
        var page = await _browser.NewPageAsync();

        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-child",
                        "parentId": "frame-main",
                        "loaderId": "loader-2",
                        "name": "",
                        "url": "https://example.test/child.html"
                    }
                }
            }
            """);
        _socket.Enqueue("""
            {
                "method": "Page.frameNavigated",
                "sessionId": "session-1",
                "params": {
                    "frame": {
                        "id": "frame-main",
                        "loaderId": "loader-1",
                        "name": "",
                        "url": "https://example.test/"
                    }
                }
            }
            """);
        await Task.Delay(100);

        Assert.AreEqual("https://example.test/", page.Url, "only a top-level frame becomes the main frame");
    }

    private void DeliverMainWorldContext(int contextId, string frameId)
        => _socket.Enqueue($$"""
            {
                "method": "Runtime.executionContextCreated",
                "sessionId": "session-1",
                "params": {
                    "context": {
                        "id": {{contextId}},
                        "origin": "://",
                        "name": "",
                        "auxData": { "frameId": "{{frameId}}", "isDefault": true }
                    }
                }
            }
            """);

    private static string NumberResult(int id, int value)
        => $$"""
            {
                "id": {{id}},
                "sessionId": "session-1",
                "result": { "result": { "type": "number", "value": {{value}} } }
            }
            """;

    private static string UndefinedResult(int id)
        => $$"""
            {
                "id": {{id}},
                "sessionId": "session-1",
                "result": { "result": { "type": "undefined" } }
            }
            """;

    private List<(string Method, string? SessionId)> SentMethods()
        => _socket.SentMessages
            .Select(bytes =>
            {
                using var doc = JsonDocument.Parse(bytes);
                var root = doc.RootElement;
                var sessionId = root.TryGetProperty("sessionId", out var s) ? s.GetString() : null;
                return (root.GetProperty("method").GetString()!, sessionId);
            })
            .ToList();

    private string LastSent(string method)
        => _socket.SentMessages
            .Select(bytes => Encoding.UTF8.GetString(bytes))
            .Last(json =>
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("method").GetString() == method;
            });
}
