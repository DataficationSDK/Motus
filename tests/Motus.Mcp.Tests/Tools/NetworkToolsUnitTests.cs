using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class NetworkToolsUnitTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [TestMethod]
    public async Task RouteFulfill_RegistersOnTheActiveContext()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();
        var headers = new Dictionary<string, string> { ["X-Test"] = "1" };

        var result = await RoutingTools.RouteFulfillAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            status: 201,
            body: "{\"ok\":true}",
            content_type: "application/json",
            headers: headers);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        CollectionAssert.Contains(pages.Context.RoutedPatterns, "*api*");
        StringAssert.Contains(TextOf(result), "201");

        // The registered handler fulfills with the options the tool passed through.
        var route = new FakeRoute();
        await pages.Context.Handlers["*api*"](route);
        Assert.AreEqual(201, route.FulfilledWith?.Status);
        Assert.AreEqual("application/json", route.FulfilledWith?.ContentType);
        Assert.AreEqual("1", route.FulfilledWith?.Headers?["X-Test"]);
    }

    [TestMethod]
    public async Task RouteAbort_RegistersAnAbortRule()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await RoutingTools.RouteAbortAsync(
            url_pattern: "*track*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            error_code: "blockedbyclient");

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        var route = new FakeRoute();
        await pages.Context.Handlers["*track*"](route);
        Assert.IsTrue(route.AbortCalled);
        Assert.AreEqual("blockedbyclient", route.AbortedWith);
    }

    [TestMethod]
    public async Task RouteContinue_RegistersAContinueRule()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await RoutingTools.RouteContinueAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            url: null,
            method: "POST",
            headers: null,
            post_data: "body");

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        var route = new FakeRoute();
        await pages.Context.Handlers["*api*"](route);
        Assert.IsTrue(route.ContinueCalled);
        Assert.AreEqual("POST", route.ContinuedWith?.Method);
        Assert.AreEqual("body", route.ContinuedWith?.PostData);
    }

    [TestMethod]
    public async Task Unroute_Registered_ReportsRemoved()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();
        await RoutingTools.RouteFulfillAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            status: null,
            body: null,
            content_type: null,
            headers: null);

        var result = await RoutingTools.UnrouteAsync("*api*", pages, network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "Removed");
    }

    [TestMethod]
    public async Task Unroute_Unregistered_ReportsNothing()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await RoutingTools.UnrouteAsync("*api*", pages, network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No mock");
    }

    [TestMethod]
    public async Task RouteList_Empty_ReportsNone()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await RoutingTools.RouteListAsync(pages, network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No mocks");
    }

    [TestMethod]
    public async Task RouteList_ListsPatternsAndKinds()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();
        await RoutingTools.RouteFulfillAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            status: null,
            body: null,
            content_type: null,
            headers: null);
        await RoutingTools.RouteAbortAsync(
            url_pattern: "*track*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            error_code: null);

        var result = await RoutingTools.RouteListAsync(pages, network, Ct);

        var text = TextOf(result);
        StringAssert.Contains(text, "*api* -> Fulfill");
        StringAssert.Contains(text, "*track* -> Abort");
    }

    [TestMethod]
    public void NetworkRequests_Empty_ReportsNone()
    {
        var network = new NetworkService();

        var result = NetworkTools.NetworkRequests(network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No requests");
    }

    [TestMethod]
    public void NetworkRequests_NumbersEachLine_AndRepeatsOnASecondRead()
    {
        var network = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        network.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/x", "fetch"), status: 200));

        var result = NetworkTools.NetworkRequests(network, Ct);

        StringAssert.Contains(TextOf(result), "[1] GET 200 https://api.test/x (fetch)");
        StringAssert.Contains(TextOf(result), "next=2");
        // Reading no longer empties the log, so the same read can be made again.
        StringAssert.Contains(TextOf(NetworkTools.NetworkRequests(network, Ct)), "[1] GET 200");
    }

    [TestMethod]
    public void NetworkRequests_WithSince_ReturnsOnlyWhatFollowedTheCursor()
    {
        var network = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        network.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/first")));
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/second")));

        var text = TextOf(NetworkTools.NetworkRequests(network, Ct, since: 2));

        StringAssert.Contains(text, "/second");
        Assert.IsFalse(text.Contains("/first", StringComparison.Ordinal), text);
        StringAssert.Contains(TextOf(NetworkTools.NetworkRequests(network, Ct, since: 3)), "No requests");
    }

    [TestMethod]
    public async Task NetworkRequest_ReportsHeadersBodyAndTheMissingSequence()
    {
        var network = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        network.SubscribePage(page);

        var request = new FakeRequest("POST", "https://api.test/orders", "fetch")
        {
            PostData = "{\"id\":7}",
            Headers = new FakeHeaders([new("accept", "application/json")]),
        };
        page.RaiseResponse(new FakeResponse(request, status: 201)
        {
            Headers = new FakeHeaders([new("content-type", "application/json")]),
            Body = "{\"ok\":true}",
        });

        var text = TextOf(await NetworkTools.NetworkRequestAsync(1, network, Ct));

        StringAssert.Contains(text, "[1] POST 201 https://api.test/orders (fetch)");
        StringAssert.Contains(text, "  accept: application/json");
        StringAssert.Contains(text, "  content-type: application/json");
        StringAssert.Contains(text, "Request body:");
        StringAssert.Contains(text, "{\"id\":7}");
        StringAssert.Contains(text, "{\"ok\":true}");

        var missing = await NetworkTools.NetworkRequestAsync(42, network, Ct);
        Assert.IsTrue(missing.IsError ?? false);
        StringAssert.Contains(TextOf(missing), "No request with sequence 42");
    }

    [TestMethod]
    public async Task NetworkRequest_WhenTheBodyIsGone_SaysSoRatherThanFailing()
    {
        var network = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        network.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/x")));

        var result = await NetworkTools.NetworkRequestAsync(1, network, Ct);

        Assert.IsFalse(result.IsError ?? false, TextOf(result));
        StringAssert.Contains(TextOf(result), "Response body: the browser no longer has it");
    }

    [TestMethod]
    public async Task NetworkRequest_DoesNotFetchABodyItWouldNotPrint()
    {
        var network = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        network.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://cdn.test/logo.png", "image"))
        {
            Headers = new FakeHeaders([new("content-type", "image/png")]),
            Body = "binary",
        });

        var text = TextOf(await NetworkTools.NetworkRequestAsync(1, network, Ct));

        StringAssert.Contains(text, "Response body: not shown, because it is image/png.");
        Assert.IsFalse(text.Contains("binary", StringComparison.Ordinal), text);
    }

    [TestMethod]
    public async Task RouteFulfill_ContextResolutionFails_ReturnsError()
    {
        var pages = new ThrowingContextService();
        var network = new NetworkService();

        var result = await RoutingTools.RouteFulfillAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            status: null,
            body: null,
            content_type: null,
            headers: null);

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "boom");
    }

    /// <summary>A page service whose active-context resolution fails, to exercise the tools' error path.</summary>
    private sealed class ThrowingContextService : ActivePageService
    {
        public ThrowingContextService()
            : base(new BrowserSessionManager(new McpServerLaunchOptions()))
        {
        }

        public override Task<IBrowserContext> GetOrCreateActiveContextAsync(CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("boom");
    }

    // --- interception and the local filesystem ---

    [TestMethod]
    public async Task RouteFulfill_RedirectingToAFileUrl_IsRefusedWithoutRegistering()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await RoutingTools.RouteFulfillAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            status: 302,
            headers: new Dictionary<string, string> { ["Location"] = "file:///etc/hosts" });

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "file:// navigation is disabled");
        Assert.AreEqual(0, pages.Context.RoutedPatterns.Count);
    }

    [TestMethod]
    public async Task RouteContinue_OverridingTheUrlWithAFileUrl_IsRefusedWithoutRegistering()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await RoutingTools.RouteContinueAsync(
            url_pattern: "*api*",
            pageService: pages,
            networkService: network,
            cancellationToken: Ct,
            url: "file:///etc/hosts");

        Assert.IsTrue(result.IsError);
        StringAssert.Contains(TextOf(result), "file:// navigation is disabled");
        Assert.AreEqual(0, pages.Context.RoutedPatterns.Count);
    }
}
