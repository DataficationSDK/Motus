using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class NetworkServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    // --- route registry ---

    [TestMethod]
    public async Task RegisterFulfill_RoutesOnce_AndHandlerFulfillsWithOptions()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();
        var options = new RouteFulfillOptions { Status = 201, Body = "hi", ContentType = "text/plain" };

        await service.RegisterFulfillAsync(context, "*api*", options, Ct);

        CollectionAssert.AreEqual(new[] { "*api*" }, context.RoutedPatterns);

        var route = new FakeRoute();
        await context.Handlers["*api*"](route);

        Assert.IsNotNull(route.FulfilledWith);
        Assert.AreEqual(201, route.FulfilledWith.Status);
        Assert.AreEqual("hi", route.FulfilledWith.Body);
        Assert.AreEqual("text/plain", route.FulfilledWith.ContentType);
    }

    [TestMethod]
    public async Task RegisterAbort_HandlerAbortsWithErrorCode()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();

        await service.RegisterAbortAsync(context, "*track*", "blockedbyclient", Ct);
        var route = new FakeRoute();
        await context.Handlers["*track*"](route);

        Assert.IsTrue(route.AbortCalled);
        Assert.AreEqual("blockedbyclient", route.AbortedWith);
    }

    [TestMethod]
    public async Task RegisterContinue_HandlerContinuesWithOptions()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();

        await service.RegisterContinueAsync(context, "*api*", new RouteContinueOptions(Method: "POST"), Ct);
        var route = new FakeRoute();
        await context.Handlers["*api*"](route);

        Assert.IsTrue(route.ContinueCalled);
        Assert.AreEqual("POST", route.ContinuedWith?.Method);
    }

    [TestMethod]
    public async Task ReRegister_SamePattern_ReplacesRuleWithoutRoutingAgain()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();

        await service.RegisterFulfillAsync(context, "*api*", new RouteFulfillOptions { Status = 200 }, Ct);
        await service.RegisterAbortAsync(context, "*api*", "aborted", Ct);

        // The pattern is routed exactly once; the handler reads the latest rule.
        CollectionAssert.AreEqual(new[] { "*api*" }, context.RoutedPatterns);

        var route = new FakeRoute();
        await context.Handlers["*api*"](route);

        Assert.IsTrue(route.AbortCalled);
        Assert.IsNull(route.FulfilledWith);
    }

    [TestMethod]
    public async Task Unroute_RemovesRuleAndUnregisters()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();
        await service.RegisterFulfillAsync(context, "*api*", new RouteFulfillOptions(), Ct);

        var removed = await service.UnrouteAsync(context, "*api*", Ct);

        Assert.IsTrue(removed);
        CollectionAssert.AreEqual(new[] { "*api*" }, context.UnroutedPatterns);
        Assert.AreEqual(0, service.ListRoutes(context).Count);
    }

    [TestMethod]
    public async Task Unroute_UnknownPattern_ReturnsFalse()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();

        var removed = await service.UnrouteAsync(context, "*api*", Ct);

        Assert.IsFalse(removed);
        Assert.AreEqual(0, context.UnroutedPatterns.Count);
    }

    [TestMethod]
    public async Task ListRoutes_ReflectsRegistrations()
    {
        var service = new NetworkService();
        var context = new FakeBrowserContext();
        await service.RegisterFulfillAsync(context, "*api*", new RouteFulfillOptions(), Ct);
        await service.RegisterAbortAsync(context, "*track*", null, Ct);

        var routes = service.ListRoutes(context);

        Assert.AreEqual(2, routes.Count);
        var fulfill = routes.Single(r => r.Pattern == "*api*");
        var abort = routes.Single(r => r.Pattern == "*track*");
        Assert.AreEqual("Fulfill", fulfill.Kind);
        Assert.AreEqual("Abort", abort.Kind);
    }

    // --- request log ---

    [TestMethod]
    public void Response_IsLogged_AsMethodStatusUrlType()
    {
        var service = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        service.SubscribePage(page);

        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/users", "fetch"), status: 200));

        var entries = service.DrainRequests();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("GET 200 https://api.test/users (fetch)", entries[0].ToString());
    }

    [TestMethod]
    public void RequestFailed_IsLogged_AsFailed()
    {
        var service = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        service.SubscribePage(page);

        page.RaiseRequestFailed(new FakeRequest("GET", "https://api.test/x", "fetch"));

        var entries = service.DrainRequests();
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("GET FAILED https://api.test/x (fetch)", entries[0].ToString());
    }

    [TestMethod]
    public void Drain_ClearsTheLog()
    {
        var service = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        service.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest()));

        Assert.AreEqual(1, service.DrainRequests().Count);
        Assert.AreEqual(0, service.DrainRequests().Count);
    }

    [TestMethod]
    public void Log_IsBounded_EvictingOldest()
    {
        var service = new NetworkService();
        var page = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        service.SubscribePage(page);

        for (var i = 0; i < 260; i++)
            page.RaiseResponse(new FakeResponse(new FakeRequest("GET", $"https://api.test/{i}")));

        var entries = service.DrainRequests();
        Assert.AreEqual(250, entries.Count);
        // The first ten were evicted; the oldest kept entry is request 10.
        StringAssert.Contains(entries[0].ToString(), "/10 ");
    }

    [TestMethod]
    public void SubscribingNewPage_DetachesThePrevious()
    {
        var service = new NetworkService();
        var first = new FakeToolPage(new AccessibilitySnapshot([], 0, null));
        var second = new FakeToolPage(new AccessibilitySnapshot([], 0, null));

        service.SubscribePage(first);
        service.SubscribePage(second);
        first.RaiseResponse(new FakeResponse(new FakeRequest()));

        Assert.AreEqual(0, service.DrainRequests().Count);
    }
}
