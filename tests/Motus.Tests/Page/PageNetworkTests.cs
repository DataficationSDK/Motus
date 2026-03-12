using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Page;

[TestClass]
public class PageNetworkTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private CdpSessionRegistry _registry = null!;
    private Motus.Browser _browser = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        _registry = new CdpSessionRegistry(_transport);

        _browser = new Motus.Browser(
            _transport, _registry, process: null, tempUserDataDir: null,
            handleSigint: false, handleSigterm: false);

        var initTask = _browser.InitializeAsync(CancellationToken.None);
        _socket.Enqueue("""{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await initTask;
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        await _transport.DisposeAsync();
    }

    private async Task<IPage> CreatePageAsync()
    {
        _socket.QueueResponse("""{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.QueueResponse("""{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.QueueResponse("""{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.QueueResponse("""{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.QueueResponse("""{"id": 8, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    [TestMethod]
    public async Task RequestEvent_FiresOnNetworkRequestWillBeSent()
    {
        var page = await CreatePageAsync();

        RequestEventArgs? received = null;
        page.Request += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": {
                        "url": "https://example.com/api/data",
                        "method": "GET"
                    },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "XHR"
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual("https://example.com/api/data", received.Request.Url);
        Assert.AreEqual("GET", received.Request.Method);
        Assert.AreEqual("XHR", received.Request.ResourceType);
        Assert.IsFalse(received.Request.IsNavigationRequest);
    }

    [TestMethod]
    public async Task ResponseEvent_FiresOnNetworkResponseReceived()
    {
        var page = await CreatePageAsync();

        ResponseEventArgs? received = null;
        page.Response += (_, args) => received = args;

        // First send the request
        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com/page", "method": "GET" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "Document"
                }
            }
            """);

        await Task.Delay(100);

        // Then send the response
        _socket.Enqueue("""
            {
                "method": "Network.responseReceived",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "timestamp": 1001.0,
                    "type": "Document",
                    "response": {
                        "url": "https://example.com/page",
                        "status": 200,
                        "statusText": "OK",
                        "headers": { "content-type": "text/html" }
                    }
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual(200, received.Response.Status);
        Assert.AreEqual("OK", received.Response.StatusText);
        Assert.IsTrue(received.Response.Ok);
        Assert.AreEqual("https://example.com/page", received.Response.Url);
    }

    [TestMethod]
    public async Task RequestFailedEvent_FiresOnNetworkLoadingFailed()
    {
        var page = await CreatePageAsync();

        RequestEventArgs? received = null;
        page.RequestFailed += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com/fail", "method": "GET" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "XHR"
                }
            }
            """);

        await Task.Delay(100);

        _socket.Enqueue("""
            {
                "method": "Network.loadingFailed",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "timestamp": 1001.0,
                    "type": "XHR",
                    "errorText": "net::ERR_CONNECTION_REFUSED"
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual("https://example.com/fail", received.Request.Url);
    }

    [TestMethod]
    public async Task RequestFinishedEvent_FiresOnNetworkLoadingFinished()
    {
        var page = await CreatePageAsync();

        RequestEventArgs? received = null;
        page.RequestFinished += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com/done", "method": "GET" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "Script"
                }
            }
            """);

        await Task.Delay(100);

        _socket.Enqueue("""
            {
                "method": "Network.loadingFinished",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "timestamp": 1001.0,
                    "encodedDataLength": 1234
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual("https://example.com/done", received.Request.Url);
    }

    [TestMethod]
    public async Task WaitForRequestAsync_CompletesOnMatchingUrl()
    {
        var page = await CreatePageAsync();

        var requestTask = page.WaitForRequestAsync("**/api/**");

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com/api/users", "method": "POST" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "XHR"
                }
            }
            """);

        var request = await requestTask;
        Assert.AreEqual("https://example.com/api/users", request.Url);
        Assert.AreEqual("POST", request.Method);
    }

    [TestMethod]
    public async Task WaitForResponseAsync_CompletesOnMatchingUrl()
    {
        var page = await CreatePageAsync();

        var responseTask = page.WaitForResponseAsync("**/api/**");

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com/api/data", "method": "GET" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "XHR"
                }
            }
            """);

        await Task.Delay(100);

        _socket.Enqueue("""
            {
                "method": "Network.responseReceived",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-1",
                    "loaderId": "loader-1",
                    "timestamp": 1001.0,
                    "type": "XHR",
                    "response": {
                        "url": "https://example.com/api/data",
                        "status": 200,
                        "statusText": "OK"
                    }
                }
            }
            """);

        var response = await responseTask;
        Assert.AreEqual(200, response.Status);
    }

    [TestMethod]
    public async Task WaitForRequestAsync_TimesOut()
    {
        var page = await CreatePageAsync();

        await Assert.ThrowsExceptionAsync<TimeoutException>(
            () => page.WaitForRequestAsync("**/never-matches/**", timeout: 100));
    }

    [TestMethod]
    public async Task RouteAsync_FulfillRequest_SendsFulfillCommand()
    {
        var page = await CreatePageAsync();

        // RouteAsync enables Fetch - queue the Fetch.enable response
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");

        await page.RouteAsync("**/api/**", async route =>
        {
            // Queue the Fetch.fulfillRequest response
            _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """{"mock": true}"""
            });
        });

        // Inject a Fetch.requestPaused event
        _socket.Enqueue("""
            {
                "method": "Fetch.requestPaused",
                "sessionId": "session-1",
                "params": {
                    "requestId": "fetch-1",
                    "request": { "url": "https://example.com/api/data", "method": "GET" },
                    "frameId": "frame-1",
                    "resourceType": "XHR"
                }
            }
            """);

        await Task.Delay(300);

        // Verify Fetch.fulfillRequest was sent
        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Fetch.fulfillRequest"))
            {
                found = true;
                Assert.IsTrue(json.Contains("fetch-1"));
                break;
            }
        }
        Assert.IsTrue(found, "Expected Fetch.fulfillRequest command");
    }

    [TestMethod]
    public async Task RouteAsync_AbortRequest_SendsFailCommand()
    {
        var page = await CreatePageAsync();

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");

        await page.RouteAsync("**/block/**", async route =>
        {
            _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
            await route.AbortAsync("connectionrefused");
        });

        _socket.Enqueue("""
            {
                "method": "Fetch.requestPaused",
                "sessionId": "session-1",
                "params": {
                    "requestId": "fetch-2",
                    "request": { "url": "https://example.com/block/this", "method": "GET" },
                    "frameId": "frame-1",
                    "resourceType": "XHR"
                }
            }
            """);

        await Task.Delay(300);

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Fetch.failRequest"))
            {
                found = true;
                Assert.IsTrue(json.Contains("ConnectionRefused"));
                break;
            }
        }
        Assert.IsTrue(found, "Expected Fetch.failRequest command");
    }

    [TestMethod]
    public async Task RouteAsync_ContinueRequest_SendsContinueCommand()
    {
        var page = await CreatePageAsync();

        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");

        await page.RouteAsync("**/*", async route =>
        {
            _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
            await route.ContinueAsync(new RouteContinueOptions(
                Url: "https://example.com/modified"));
        });

        _socket.Enqueue("""
            {
                "method": "Fetch.requestPaused",
                "sessionId": "session-1",
                "params": {
                    "requestId": "fetch-3",
                    "request": { "url": "https://example.com/original", "method": "GET" },
                    "frameId": "frame-1",
                    "resourceType": "Document"
                }
            }
            """);

        await Task.Delay(300);

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Fetch.continueRequest") && json.Contains("modified"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Fetch.continueRequest command with modified URL");
    }

    [TestMethod]
    public async Task UnrouteAsync_DisablesFetchWhenNoRoutesRemain()
    {
        var page = await CreatePageAsync();

        // Enable Fetch
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        Func<IRoute, Task> handler = route => route.ContinueAsync();
        await page.RouteAsync("**/*", handler);

        // Disable Fetch
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
        await page.UnrouteAsync("**/*", handler);

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Fetch.disable"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected Fetch.disable command");
    }

    [TestMethod]
    public async Task UnmatchedFetchRequest_AutoContinues()
    {
        var page = await CreatePageAsync();

        // Register a route for a specific pattern
        _socket.QueueResponse("""{"id": 9, "sessionId": "session-1", "result": {}}""");
        await page.RouteAsync("**/api/specific", route => route.FulfillAsync());

        // Send a Fetch event that does NOT match the pattern
        _socket.QueueResponse("""{"id": 10, "sessionId": "session-1", "result": {}}""");
        _socket.Enqueue("""
            {
                "method": "Fetch.requestPaused",
                "sessionId": "session-1",
                "params": {
                    "requestId": "fetch-nomatch",
                    "request": { "url": "https://example.com/other/path", "method": "GET" },
                    "frameId": "frame-1",
                    "resourceType": "Script"
                }
            }
            """);

        await Task.Delay(300);

        var found = false;
        for (int i = 0; i < _socket.SentMessages.Count; i++)
        {
            var json = _socket.GetSentJson(i);
            if (json.Contains("Fetch.continueRequest") && json.Contains("fetch-nomatch"))
            {
                found = true;
                break;
            }
        }
        Assert.IsTrue(found, "Expected auto-continue for unmatched request");
    }

    [TestMethod]
    public async Task NavigationRequest_SetsIsNavigationRequest()
    {
        var page = await CreatePageAsync();

        RequestEventArgs? received = null;
        page.Request += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "nav-1",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com", "method": "GET" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "Document"
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.IsTrue(received.Request.IsNavigationRequest);
    }

    [TestMethod]
    public async Task ResponseLinkedToRequest()
    {
        var page = await CreatePageAsync();

        ResponseEventArgs? received = null;
        page.Response += (_, args) => received = args;

        _socket.Enqueue("""
            {
                "method": "Network.requestWillBeSent",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-link",
                    "loaderId": "loader-1",
                    "documentURL": "https://example.com",
                    "request": { "url": "https://example.com/linked", "method": "GET" },
                    "timestamp": 1000.0,
                    "wallTime": 1000.0,
                    "type": "XHR"
                }
            }
            """);
        await Task.Delay(100);

        _socket.Enqueue("""
            {
                "method": "Network.responseReceived",
                "sessionId": "session-1",
                "params": {
                    "requestId": "req-link",
                    "loaderId": "loader-1",
                    "timestamp": 1001.0,
                    "response": {
                        "url": "https://example.com/linked",
                        "status": 201,
                        "statusText": "Created"
                    }
                }
            }
            """);

        await Task.Delay(150);

        Assert.IsNotNull(received);
        Assert.AreEqual("https://example.com/linked", received.Response.Request.Url);
        Assert.IsNotNull(received.Response.Request.Response);
        Assert.AreEqual(201, received.Response.Request.Response.Status);
    }
}
