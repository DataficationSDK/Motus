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

        var result = await NetworkTools.RouteFulfillAsync(
            "*api*", 201, "{\"ok\":true}", "application/json", headers, pages, network, Ct);

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

        var result = await NetworkTools.RouteAbortAsync("*track*", "blockedbyclient", pages, network, Ct);

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

        var result = await NetworkTools.RouteContinueAsync("*api*", null, "POST", null, "body", pages, network, Ct);

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
        await NetworkTools.RouteFulfillAsync("*api*", null, null, null, null, pages, network, Ct);

        var result = await NetworkTools.UnrouteAsync("*api*", pages, network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "Removed");
    }

    [TestMethod]
    public async Task Unroute_Unregistered_ReportsNothing()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await NetworkTools.UnrouteAsync("*api*", pages, network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No mock");
    }

    [TestMethod]
    public async Task RouteList_Empty_ReportsNone()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        var result = await NetworkTools.RouteListAsync(pages, network, Ct);

        Assert.IsFalse(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "No mocks");
    }

    [TestMethod]
    public async Task RouteList_ListsPatternsAndKinds()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();
        await NetworkTools.RouteFulfillAsync("*api*", null, null, null, null, pages, network, Ct);
        await NetworkTools.RouteAbortAsync("*track*", null, pages, network, Ct);

        var result = await NetworkTools.RouteListAsync(pages, network, Ct);

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
    public void NetworkRequests_RendersAndDrainsTheLog()
    {
        var network = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        network.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/x", "fetch"), status: 200));

        var result = NetworkTools.NetworkRequests(network, Ct);

        StringAssert.Contains(TextOf(result), "GET 200 https://api.test/x (fetch)");
        // Draining clears, so a second read reports nothing.
        StringAssert.Contains(TextOf(NetworkTools.NetworkRequests(network, Ct)), "No requests");
    }

    [TestMethod]
    public async Task RouteFulfill_ContextResolutionFails_ReturnsError()
    {
        var pages = new ThrowingContextService();
        var network = new NetworkService();

        var result = await NetworkTools.RouteFulfillAsync("*api*", null, null, null, null, pages, network, Ct);

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
}
