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
        var page = NewPage();
        service.SubscribePage(page);

        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/users", "fetch"), status: 200));

        var entries = service.ReadRequests().Entries;
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("GET 200 https://api.test/users (fetch)", entries[0].ToString());
        Assert.AreEqual(1, entries[0].Sequence);
    }

    [TestMethod]
    public void RequestFailed_IsLogged_AsFailed()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);

        page.RaiseRequestFailed(new FakeRequest("GET", "https://api.test/x", "fetch"));

        var entries = service.ReadRequests().Entries;
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("GET FAILED https://api.test/x (fetch)", entries[0].ToString());
    }

    [TestMethod]
    public void HeadersAndPostData_AreCapturedWithTheEntry()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);

        var request = new FakeRequest("POST", "https://api.test/orders", "fetch")
        {
            PostData = "{\"id\":7}",
            Headers = new FakeHeaders([new("content-type", "application/json")]),
        };
        page.RaiseResponse(new FakeResponse(request, status: 201)
        {
            Headers = new FakeHeaders([new("content-type", "application/json"), new("content-length", "12")]),
        });

        var entry = service.ReadRequests().Entries.Single();
        Assert.AreEqual("{\"id\":7}", entry.PostData);
        Assert.AreEqual("application/json", entry.RequestHeaders.Single().Value);
        Assert.AreEqual("application/json", entry.ResponseHeader("Content-Type"), "header lookup ignores case.");
        Assert.AreEqual("12", entry.ResponseHeader("content-length"));
        Assert.IsNull(entry.ResponseHeader("etag"));
    }

    [TestMethod]
    public void Reading_LeavesTheLogInPlace_AndTheCursorReturnsOnlyWhatFollowed()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/first")));

        var first = service.ReadRequests();
        Assert.AreEqual(1, first.Entries.Count);
        Assert.AreEqual(1, service.ReadRequests().Entries.Count, "a second read sees the same entry.");

        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/second")));

        var since = service.ReadRequests(first.Next);
        Assert.AreEqual(1, since.Entries.Count);
        StringAssert.Contains(since.Entries[0].Url, "/second");
    }

    [TestMethod]
    public void FindRequest_ReturnsTheEntryWithThatSequence()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/a")));
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/b")));

        Assert.AreEqual("https://api.test/b", service.FindRequest(2)?.Url);
        Assert.IsNull(service.FindRequest(99), "a sequence the log never held is not an entry.");
    }

    [TestMethod]
    public async Task TryReadBody_ReturnsTheBody_AndNullWhenTheBrowserNoLongerHasIt()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);

        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/a")) { Body = "{\"ok\":true}" });
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/b")));
        page.RaiseRequestFailed(new FakeRequest("GET", "https://api.test/c"));

        Assert.AreEqual("{\"ok\":true}", await service.TryReadBodyAsync(1, Ct));
        Assert.IsNull(await service.TryReadBodyAsync(2, Ct), "an evicted body is reported as missing, not thrown.");
        Assert.IsNull(await service.TryReadBodyAsync(3, Ct), "a failed request has no response to ask.");
        Assert.IsNull(await service.TryReadBodyAsync(99, Ct));
    }

    [TestMethod]
    public void Log_IsBounded_EvictingOldest()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);

        for (var i = 0; i < 260; i++)
            page.RaiseResponse(new FakeResponse(new FakeRequest("GET", $"https://api.test/{i}")));

        var slice = service.ReadRequests();
        Assert.AreEqual(250, slice.Entries.Count);
        // The first ten were evicted; the oldest kept entry is request 10.
        StringAssert.Contains(slice.Entries[0].ToString(), "/10 ");
        Assert.AreEqual(11, slice.Entries[0].Sequence);
        Assert.AreEqual(261, slice.Next);
    }

    [TestMethod]
    public void Read_WithACursorTheLogHasPassed_SaysHowMuchWasMissed()
    {
        var service = new NetworkService();
        var page = NewPage();
        service.SubscribePage(page);
        page.RaiseResponse(new FakeResponse(new FakeRequest("GET", "https://api.test/first")));

        var cursor = service.ReadRequests().Next;
        for (var i = 0; i < 260; i++)
            page.RaiseResponse(new FakeResponse(new FakeRequest("GET", $"https://api.test/{i}")));

        var slice = service.ReadRequests(cursor);
        Assert.AreEqual(250, slice.Entries.Count);
        Assert.AreEqual(10, slice.Dropped);
        StringAssert.Contains(slice.Entries[0].ToString(), "/10 ");
    }

    [TestMethod]
    public void SubscribingNewPage_DetachesThePrevious()
    {
        var service = new NetworkService();
        var first = NewPage();
        var second = NewPage();

        service.SubscribePage(first);
        service.SubscribePage(second);
        first.RaiseResponse(new FakeResponse(new FakeRequest()));

        Assert.AreEqual(0, service.ReadRequests().Entries.Count);
    }

    private static FakeToolPage NewPage() => new(new AccessibilitySnapshot([], 0, null));
}
