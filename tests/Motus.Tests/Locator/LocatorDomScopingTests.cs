using System.Text.Json;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Locator;

[TestClass]
public class LocatorDomScopingTests
{
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
        _browser = new Motus.Browser(_transport, _registry, process: null, tempUserDataDir: null,
                                     handleSigint: false, handleSigterm: false);
        _socket.Respond(1, """{"id": 1, "result": {"protocolVersion":"1.3","product":"Chrome/120","revision":"@x","userAgent":"UA","jsVersion":"12"}}""");
        await _browser.InitializeAsync(CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    private async Task<IPage> CreatePageAsync()
    {
        _socket.Respond(2, """{"id": 2, "result": {"browserContextId": "ctx-1"}}""");
        _socket.Respond(3, """{"id": 3, "result": {"targetId": "target-1"}}""");
        _socket.Respond(4, """{"id": 4, "result": {"sessionId": "session-1"}}""");
        _socket.Respond(5, """{"id": 5, "sessionId": "session-1", "result": {}}""");
        _socket.Respond(6, """{"id": 6, "sessionId": "session-1", "result": {}}""");
        _socket.Respond(7, """{"id": 7, "sessionId": "session-1", "result": {}}""");
        _socket.Respond(8, """{"id": 8, "sessionId": "session-1", "result": {}}""");
        return await _browser.NewPageAsync();
    }

    // Reusable response fragments.
    private static string EvalReturnsArray(int id, string arrObjectId)
        => @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": {""type"": ""object"", ""objectId"": """ + arrObjectId + @"""}}}";

    private static string CallFunctionOnReturnsArray(int id, string arrObjectId)
        => @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": {""type"": ""object"", ""objectId"": """ + arrObjectId + @"""}}}";

    private static string GetPropertiesReturnsElements(int id, params string[] elementObjectIds)
    {
        var items = string.Join(", ", elementObjectIds.Select((oid, i) =>
            @"{""name"": """ + i + @""", ""value"": {""type"": ""object"", ""objectId"": """ + oid + @"""}}"));
        var prefix = elementObjectIds.Length == 0 ? "" : items + ", ";
        return @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": [" + prefix +
               @"{""name"": ""length"", ""value"": {""type"": ""number"", ""value"": " + elementObjectIds.Length + @"}}]}}";
    }

    private static string TextContentResult(int id, string value)
        => @"{""id"": " + id + @", ""sessionId"": ""session-1"", ""result"": {""result"": {""type"": ""string"", ""value"": """ + value + @"""}}}";

    /// <summary>
    /// Configures the socket to answer a single-parent scoped chain: strategy resolve returns one
    /// parent, the descendant query returns the given child object ids, then a TextContent read
    /// returns <paramref name="textValue"/> against the first child.
    /// </summary>
    private void SetupSingleParentChain(string parentObjectId, string[] childObjectIds, string textValue)
    {
        _socket.SetHandler(envelope =>
        {
            var id = envelope.GetProperty("id").GetInt32();
            var method = envelope.GetProperty("method").GetString()!;
            return method switch
            {
                "Runtime.evaluate" => EvalReturnsArray(id, "arr-parent"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-parent"
                    => GetPropertiesReturnsElements(id, parentObjectId),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-children"
                    => GetPropertiesReturnsElements(id, childObjectIds),
                "Runtime.callFunctionOn" when envelope.GetProperty("params").GetProperty("objectId").GetString() == parentObjectId
                    => CallFunctionOnReturnsArray(id, "arr-children"),
                "Runtime.callFunctionOn" => TextContentResult(id, textValue),
                _ => throw new InvalidOperationException($"Unexpected CDP method: {method}"),
            };
        });
    }

    // --- Scoped chain under different parent strategies ---

    [TestMethod]
    public async Task ScopedChain_CssParent_QueriesDescendantsUnderParent()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.row").Locator(".cell");
        SetupSingleParentChain("row-1", ["cell-1"], "cell text");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("cell text", text);

        // Verify the descendant query ran against the resolved parent via callFunctionOn, carrying
        // the child selector, not a concatenated CSS string at the document root.
        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        Assert.IsTrue(sent.Any(s => s.Contains("\"method\":\"Runtime.callFunctionOn\"")
                                    && s.Contains("\"objectId\":\"row-1\"")
                                    && s.Contains("querySelectorAll")
                                    && s.Contains(".cell")));
    }

    [TestMethod]
    public async Task ScopedChain_XPathParent_QueriesDescendantsUnderResolvedElement()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("xpath=//section[@id='panel']").Locator(".cell");
        SetupSingleParentChain("panel-1", ["cell-1"], "panel cell");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("panel cell", text);

        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        Assert.IsTrue(sent.Any(s => s.Contains("document.evaluate")), "XPath strategy should be invoked for the parent resolve.");
        Assert.IsTrue(sent.Any(s => s.Contains("\"objectId\":\"panel-1\"") && s.Contains("querySelectorAll")),
            "Descendant query should run under the resolved XPath element.");
    }

    [TestMethod]
    public async Task ScopedChain_TestIdParent_QueriesDescendantsUnderResolvedElement()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("data-testid=grid").Locator(".row");
        SetupSingleParentChain("grid-1", ["row-1"], "row");

        var text = await locator.TextContentAsync();
        Assert.AreEqual("row", text);

        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        Assert.IsTrue(sent.Any(s => s.Contains("data-testid") && s.Contains("grid")),
            "data-testid strategy should be invoked for the parent resolve.");
        Assert.IsTrue(sent.Any(s => s.Contains("\"objectId\":\"grid-1\"") && s.Contains("querySelectorAll")));
    }

    // --- Nth-on-base preserved through scoped chain ---

    [TestMethod]
    public async Task ScopedChain_FirstOnBase_ScopesToFirstParentOnly()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.row").First.Locator(".cell");

        // Base resolves to TWO rows — .First should pick row-A only, so descendant query runs once.
        _socket.SetHandler(envelope =>
        {
            var id = envelope.GetProperty("id").GetInt32();
            var method = envelope.GetProperty("method").GetString()!;
            return method switch
            {
                "Runtime.evaluate" => EvalReturnsArray(id, "arr-rows"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-rows"
                    => GetPropertiesReturnsElements(id, "row-A", "row-B"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-cells-A"
                    => GetPropertiesReturnsElements(id, "cell-A1"),
                "Runtime.callFunctionOn" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "row-A"
                    => CallFunctionOnReturnsArray(id, "arr-cells-A"),
                "Runtime.callFunctionOn" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "cell-A1"
                    => TextContentResult(id, "A1"),
                _ => throw new InvalidOperationException($"Unexpected CDP call: method={method}, params={envelope.GetProperty("params")}"),
            };
        });

        var text = await locator.TextContentAsync();
        Assert.AreEqual("A1", text);

        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        Assert.IsFalse(sent.Any(s => s.Contains("\"objectId\":\"row-B\"")),
            "First() should restrict descendant query to the first base match only.");
    }

    // --- Multi-parent flatten and dedupe ---

    [TestMethod]
    public async Task ScopedChain_MultipleBaseMatches_FlattensDescendantsDeduped()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.row").Locator(".cell");

        // Row A's descendants: [cell-1, cell-shared]; row B's: [cell-shared, cell-2].
        // After dedupe we expect 3 distinct handles total.
        _socket.SetHandler(envelope =>
        {
            var id = envelope.GetProperty("id").GetInt32();
            var method = envelope.GetProperty("method").GetString()!;
            return method switch
            {
                "Runtime.evaluate" => EvalReturnsArray(id, "arr-rows"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-rows"
                    => GetPropertiesReturnsElements(id, "row-A", "row-B"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-cells-A"
                    => GetPropertiesReturnsElements(id, "cell-1", "cell-shared"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-cells-B"
                    => GetPropertiesReturnsElements(id, "cell-shared", "cell-2"),
                "Runtime.callFunctionOn" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "row-A"
                    => CallFunctionOnReturnsArray(id, "arr-cells-A"),
                "Runtime.callFunctionOn" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "row-B"
                    => CallFunctionOnReturnsArray(id, "arr-cells-B"),
                _ => throw new InvalidOperationException($"Unexpected CDP call: method={method}"),
            };
        });

        var handles = await locator.ElementHandlesAsync();

        Assert.AreEqual(3, handles.Count);
        var ids = handles.Select(h => ((ElementHandle)h).ObjectId).ToHashSet();
        CollectionAssert.AreEquivalent(new[] { "cell-1", "cell-shared", "cell-2" }, ids.ToArray());
    }

    // --- Shadow piercing ---

    [TestMethod]
    public async Task ScopedChain_PierceShadowDefault_UsesQueryShadowInDescendantJs()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.host").Locator(".inner");
        SetupSingleParentChain("host-1", ["inner-1"], "x");

        await locator.TextContentAsync();

        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        Assert.IsTrue(sent.Any(s => s.Contains("queryShadow") && s.Contains("\"objectId\":\"host-1\"") && s.Contains(".inner")));
    }

    [TestMethod]
    public async Task ScopedChain_PierceShadowFalse_UsesPlainQuerySelectorAll()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.host", new LocatorOptions { PierceShadow = false }).Locator(".inner");
        SetupSingleParentChain("host-1", ["inner-1"], "x");

        await locator.TextContentAsync();

        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        var descendantCall = sent.First(s => s.Contains("\"objectId\":\"host-1\"") && s.Contains("Runtime.callFunctionOn"));
        Assert.IsFalse(descendantCall.Contains("queryShadow"),
            "PierceShadow=false should skip the shadow-walking variant of the descendant query.");
        Assert.IsTrue(descendantCall.Contains("querySelectorAll"));
    }

    // --- Zero-parent-match short circuit ---

    [TestMethod]
    public async Task ScopedChain_BaseMatchesZero_ThrowsWithNoDescendantCall()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.missing").Locator(".cell");

        _socket.SetHandler(envelope =>
        {
            var id = envelope.GetProperty("id").GetInt32();
            var method = envelope.GetProperty("method").GetString()!;
            return method switch
            {
                "Runtime.evaluate" => EvalReturnsArray(id, "arr-empty"),
                "Runtime.getProperties" => GetPropertiesReturnsElements(id),
                _ => throw new InvalidOperationException($"Unexpected CDP call when base matches zero: method={method}"),
            };
        });

        var ex = await Assert.ThrowsExceptionAsync<ElementNotFoundException>(() => locator.TextContentAsync());
        StringAssert.Contains(ex.Message, "No element matches parent selector", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "No descendant query was attempted", StringComparison.Ordinal);

        var sent = _socket.SentMessages.Select(b => System.Text.Encoding.UTF8.GetString(b)).ToList();
        Assert.IsFalse(sent.Any(s => s.Contains("Runtime.callFunctionOn")),
            "No descendant query should run when the base match set is empty.");
    }

    // --- Portal-aware diagnostic when parents match but descendants are empty ---

    [TestMethod]
    public async Task ScopedChain_ParentsMatchButDescendantsEmpty_ThrowsWithPortalHint()
    {
        var page = await CreatePageAsync();
        var locator = page.Locator("div.widget").Locator("#portaled");

        _socket.SetHandler(envelope =>
        {
            var id = envelope.GetProperty("id").GetInt32();
            var method = envelope.GetProperty("method").GetString()!;
            return method switch
            {
                "Runtime.evaluate" => EvalReturnsArray(id, "arr-widgets"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-widgets"
                    => GetPropertiesReturnsElements(id, "widget-1"),
                "Runtime.getProperties" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "arr-empty"
                    => GetPropertiesReturnsElements(id),
                "Runtime.callFunctionOn" when envelope.GetProperty("params").GetProperty("objectId").GetString() == "widget-1"
                    => CallFunctionOnReturnsArray(id, "arr-empty"),
                _ => throw new InvalidOperationException($"Unexpected CDP call: method={method}"),
            };
        });

        var ex = await Assert.ThrowsExceptionAsync<ElementNotFoundException>(() => locator.TextContentAsync());
        StringAssert.Contains(ex.Message, "matched 1 element(s)", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "portal", StringComparison.Ordinal);
        StringAssert.Contains(ex.Message, "#portaled", StringComparison.Ordinal);
    }
}
