using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// What a tool says when an argument it cannot work without is missing. The message has to name
/// the argument, because an agent recovers by filling that argument in.
/// </summary>
/// <remarks>
/// Before the guard, a missing ref reached a dictionary lookup and came back as
/// <c>Value cannot be null. (Parameter 'key')</c>: a sentence about the server's own internals
/// that names no field the caller sent. The sweep at the end of this class is the check that no
/// tool has been added since without one.
/// </remarks>
[TestClass]
public class ToolArgumentTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static FakeActivePageService Pages()
        => new(new FakeToolPage(new AccessibilitySnapshot([], 0, null)));

    private static string TextOf(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static void AssertRequires(string argument, CallToolResult result)
    {
        Assert.IsTrue(result.IsError ?? false, $"a missing '{argument}' is an error the agent can fix.");
        Assert.AreEqual($"The '{argument}' argument is required.", TextOf(result));
    }

    [TestMethod]
    public async Task Navigate_WithoutAUrl_NamesTheArgument()
        => AssertRequires("url", await CoreTools.NavigateAsync(null!, Pages(), Ct));

    [TestMethod]
    public async Task Click_WithoutARef_NamesTheArgument()
        => AssertRequires("ref", await CoreTools.ClickAsync(null!, Pages(), Ct, @double: null));

    [TestMethod]
    public async Task Type_WithoutARefOrText_NamesEachArgument()
    {
        AssertRequires("ref", await CoreTools.TypeAsync(
            null!, "hello", Pages(), Ct, submit: null, slowly: null));
        AssertRequires("text", await CoreTools.TypeAsync(
            "e1", null!, Pages(), Ct, submit: null, slowly: null));
    }

    [TestMethod]
    public async Task Type_WithBlankText_IsAllowed()
    {
        // A space is something a caller can mean to type, unlike a ref made of spaces.
        var pages = Pages();
        var result = await CoreTools.TypeAsync("e1", " ", pages, Ct, submit: null, slowly: null);
        Assert.AreNotEqual("The 'text' argument is required.", TextOf(result));
    }

    [TestMethod]
    public async Task RefAddressedInteractionTools_WithoutARef_NameTheArgument()
    {
        AssertRequires("ref", await InteractionTools.HoverAsync(null!, Pages(), Ct));
        AssertRequires("ref", await InteractionTools.FocusAsync(null!, Pages(), Ct));
        AssertRequires("ref", await InteractionTools.ClearAsync(null!, Pages(), Ct));
        AssertRequires("ref", await InteractionTools.ScrollIntoViewAsync(null!, Pages(), Ct));
        AssertRequires("ref", await InteractionTools.SetCheckedAsync(null!, true, Pages(), Ct));
        AssertRequires("ref", await InteractionTools.PressAsync(null!, "Enter", Pages(), Ct));
        AssertRequires("ref", await InteractionTools.SelectOptionAsync(null!, ["pro"], Pages(), Ct));
        AssertRequires("ref", await InteractionTools.UploadFilesAsync(null!, ["a.txt"], Pages(), Ct));
        AssertRequires("ref", await InteractionTools.WaitForElementAsync(null!, "visible", Pages(), Ct));
    }

    [TestMethod]
    public async Task InteractionTools_WithoutTheirOtherArguments_NameThem()
    {
        AssertRequires("key", await InteractionTools.PressAsync("e1", null!, Pages(), Ct));
        AssertRequires("key", await InteractionTools.PressKeyAsync(null!, Pages(), Ct));
        AssertRequires("values", await InteractionTools.SelectOptionAsync("e1", null!, Pages(), Ct));
        AssertRequires("values", await InteractionTools.SelectOptionAsync("e1", [], Pages(), Ct));
        AssertRequires("paths", await InteractionTools.UploadFilesAsync("e1", null!, Pages(), Ct));
        AssertRequires("state", await InteractionTools.WaitForElementAsync("e1", null!, Pages(), Ct));
    }

    [TestMethod]
    public async Task Evaluate_WithoutAnExpression_NamesTheArgument()
        => AssertRequires("expression", await PageTools.EvaluateAsync(null!, Pages(), Ct, @ref: null));

    [TestMethod]
    public async Task ContextTools_WithoutAName_NameTheArgument()
    {
        AssertRequires("name", await ContextTools.ContextCreateAsync(null!, Pages(), Ct));
        AssertRequires("name", ContextTools.ContextSelect(null!, Pages(), Ct));
        AssertRequires("name", await ContextTools.ContextCloseAsync(null!, Pages(), Ct));
    }

    [TestMethod]
    public async Task RouteTools_WithoutAPattern_NameTheArgument()
    {
        var pages = new FakeNetworkPageService();
        var network = new NetworkService();

        AssertRequires("url_pattern", await RoutingTools.RouteFulfillAsync(
            null!, pages, network, Ct, status: null, body: null, content_type: null, headers: null));
        AssertRequires("url_pattern", await RoutingTools.RouteAbortAsync(
            null!, pages, network, Ct, error_code: null));
        AssertRequires("url_pattern", await RoutingTools.RouteContinueAsync(
            null!, pages, network, Ct, url: null, method: null, headers: null, post_data: null));
        AssertRequires("url_pattern", await RoutingTools.UnrouteAsync(null!, pages, network, Ct));
    }

    [TestMethod]
    public async Task BrowserAttach_WithoutAnEndpoint_NamesTheArgument()
        => AssertRequires("endpoint", await BrowserTools.BrowserAttachAsync(null!, Pages(), Ct));

    [TestMethod]
    public async Task NoToolLeaksAParameterNameFromInsideTheServer()
    {
        var results = new List<CallToolResult>
        {
            await CoreTools.NavigateAsync(null!, Pages(), Ct),
            await CoreTools.ClickAsync(null!, Pages(), Ct, @double: null),
            await CoreTools.TypeAsync(null!, null!, Pages(), Ct, submit: null, slowly: null),
            await InteractionTools.HoverAsync(null!, Pages(), Ct),
            await InteractionTools.FocusAsync(null!, Pages(), Ct),
            await InteractionTools.ClearAsync(null!, Pages(), Ct),
            await InteractionTools.ScrollIntoViewAsync(null!, Pages(), Ct),
            await InteractionTools.SetCheckedAsync(null!, true, Pages(), Ct),
            await InteractionTools.PressAsync(null!, null!, Pages(), Ct),
            await InteractionTools.PressKeyAsync(null!, Pages(), Ct),
            await InteractionTools.SelectOptionAsync(null!, null!, Pages(), Ct),
            await InteractionTools.UploadFilesAsync(null!, null!, Pages(), Ct),
            await InteractionTools.WaitForElementAsync(null!, null!, Pages(), Ct),
            await PageTools.EvaluateAsync(null!, Pages(), Ct, @ref: null),
            await ContextTools.ContextCreateAsync(null!, Pages(), Ct),
            ContextTools.ContextSelect(null!, Pages(), Ct),
            await ContextTools.ContextCloseAsync(null!, Pages(), Ct),
            await BrowserTools.BrowserAttachAsync(null!, Pages(), Ct),
        };

        foreach (var result in results)
        {
            Assert.IsFalse(
                TextOf(result).Contains("Parameter '", StringComparison.Ordinal),
                $"a tool result named an internal parameter: {TextOf(result)}");
        }
    }
}
