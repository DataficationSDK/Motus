using Motus.Abstractions;

namespace Motus.Tests.Locator;

/// <summary>
/// Pins what the <c>GetBy*</c> locator factories match on plain HTML: implicit roles and computed
/// accessible names for <see cref="IPage.GetByRole"/>, every way of labelling a control for
/// <see cref="IPage.GetByLabel"/>, and the loose and exact text matching shared by the text-based
/// factories.
/// </summary>
/// <remarks>
/// Each element carries an id that the assertions read back, so a factory that matches the wrong
/// element gives a wrong answer rather than a right count by accident.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class GetByLocatorTests
{
    private static IBrowser? s_browser;
    private IPage? _page;

    [ClassInitialize]
    public static async Task ClassSetup(TestContext _)
    {
        try
        {
            s_browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            s_browser = null;
        }
    }

    [ClassCleanup]
    public static async Task ClassTeardown()
    {
        if (s_browser is not null)
            await s_browser.DisposeAsync();
    }

    [TestInitialize]
    public async Task Setup()
    {
        if (s_browser is null)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
            return;
        }

        _page = await s_browser.NewPageAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_page is not null)
            await _page.CloseAsync();
    }

    private async Task<IPage> ContentAsync(string html)
    {
        await _page!.SetContentAsync(html);
        return _page;
    }

    private static async Task<string[]> IdsAsync(ILocator locator)
    {
        var count = await locator.CountAsync();
        var ids = new string[count];
        for (var i = 0; i < count; i++)
            ids[i] = await locator.Nth(i).GetAttributeAsync("id") ?? "";
        return ids;
    }

    // --- GetByRole ---

    /// <summary>
    /// The repro from the issue: a plain button and a label-for input, with no role or
    /// aria-label attribute on either.
    /// </summary>
    [TestMethod]
    public async Task GetByRole_And_GetByLabel_MatchPlainHtml()
    {
        var page = await ContentAsync(
            "<form><label for=e>Email</label><input id=e>"
            + "<button id=save type=button>Save</button></form>");

        Assert.AreEqual(1, await page.GetByRole("button", "Save").CountAsync());
        Assert.AreEqual(1, await page.GetByRole("button").CountAsync());
        Assert.AreEqual(1, await page.GetByLabel("Email").CountAsync());

        Assert.AreEqual("save", await page.GetByRole("button", "Save").GetAttributeAsync("id"));
        Assert.AreEqual("e", await page.GetByRole("textbox", "Email").GetAttributeAsync("id"));
        Assert.AreEqual("e", await page.GetByLabel("Email").GetAttributeAsync("id"));
    }

    [TestMethod]
    public async Task GetByRole_MatchesImplicitRolesAndComputedNames()
    {
        var page = await ContentAsync("""
            <h2 id=h>Orders</h2>
            <a id=l href="#x">Open <b>report</b></a>
            <input id=c type=checkbox aria-labelledby=cl><span id=cl>Remember me</span>
            <img id=i alt="Company logo" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
            <div id=ri role=img aria-label="Chart"></div>
            """);

        Assert.AreEqual("h", await page.GetByRole("heading", "Orders").GetAttributeAsync("id"));
        Assert.AreEqual("l", await page.GetByRole("link", "Open report").GetAttributeAsync("id"));
        Assert.AreEqual("c", await page.GetByRole("checkbox", "Remember me").GetAttributeAsync("id"));

        // The ARIA spelling of the image role, for an <img> and an explicit role="img" alike.
        Assert.AreEqual("i", await page.GetByRole("img", "Company logo").GetAttributeAsync("id"));
        CollectionAssert.AreEqual(new[] { "i", "ri" }, await IdsAsync(page.GetByRole("img")));
    }

    [TestMethod]
    public async Task GetByRole_NameIsTheAccessibleName_NotTheAriaLabelAttribute()
    {
        var page = await ContentAsync("""
            <button id=icon aria-label="Close dialog">X</button>
            <button id=text>Close</button>
            """);

        // aria-label wins over the text, and the text alone names the second button.
        Assert.AreEqual("icon", await page.GetByRole("button", "Close dialog").GetAttributeAsync("id"));
        Assert.AreEqual("text", await page.GetByRole("button", "Close").GetAttributeAsync("id"));
        Assert.AreEqual(0, await page.GetByRole("button", "X").CountAsync());
    }

    [TestMethod]
    public async Task GetByRole_SkipsElementsHiddenFromTheAccessibilityTree()
    {
        var page = await ContentAsync("""
            <button id=shown>Go</button>
            <button id=gone style="display:none">Go</button>
            <div aria-hidden=true><button id=muted>Go</button></div>
            """);

        CollectionAssert.AreEqual(new[] { "shown" }, await IdsAsync(page.GetByRole("button", "Go")));
    }

    // --- GetByLabel ---

    [TestMethod]
    public async Task GetByLabel_MatchesEveryWayOfLabellingAControl()
    {
        var page = await ContentAsync("""
            <label for=byfor>Street</label><input id=byfor>
            <label>City <input id=wrapped></label>
            <span id=zl>Postcode</span><input id=labelledby aria-labelledby=zl>
            <input id=arialabel aria-label="Country">
            <select id=sel aria-label="Region"><option>North</option></select>
            <label for=area>Notes</label><textarea id=area></textarea>
            """);

        Assert.AreEqual("byfor", await page.GetByLabel("Street").GetAttributeAsync("id"));
        Assert.AreEqual("wrapped", await page.GetByLabel("City").GetAttributeAsync("id"));
        Assert.AreEqual("labelledby", await page.GetByLabel("Postcode").GetAttributeAsync("id"));
        Assert.AreEqual("arialabel", await page.GetByLabel("Country").GetAttributeAsync("id"));
        Assert.AreEqual("sel", await page.GetByLabel("Region").GetAttributeAsync("id"));
        Assert.AreEqual("area", await page.GetByLabel("Notes").GetAttributeAsync("id"));

        // A label is not itself labelled by its own text.
        Assert.AreEqual(1, await page.GetByLabel("Street").CountAsync());
    }

    [TestMethod]
    public async Task GetByLabel_AriaLabelledByTakesPrecedence()
    {
        var page = await ContentAsync("""
            <span id=a>First</span><span id=b>Name</span>
            <label for=x>Ignored label</label>
            <input id=x aria-labelledby="a b" aria-label="Ignored aria">
            """);

        Assert.AreEqual("x", await page.GetByLabel("First Name", exact: true).GetAttributeAsync("id"));
        Assert.AreEqual("x", await page.GetByLabel("Name", exact: true).GetAttributeAsync("id"));
        Assert.AreEqual(0, await page.GetByLabel("Ignored").CountAsync());
    }

    [TestMethod]
    public async Task GetByLabel_ExactFalse_IsCaseInsensitiveSubstring_ExactTrue_IsWholeString()
    {
        var page = await ContentAsync("""
            <label for=e>Email</label><input id=e>
            <label for=c>Confirm   email
              address</label><input id=c>
            """);

        CollectionAssert.AreEqual(new[] { "e", "c" }, await IdsAsync(page.GetByLabel("email")));
        CollectionAssert.AreEqual(new[] { "c" }, await IdsAsync(page.GetByLabel("EMAIL ADDRESS")));

        CollectionAssert.AreEqual(new[] { "e" }, await IdsAsync(page.GetByLabel("Email", exact: true)));
        Assert.AreEqual(0, await page.GetByLabel("email", exact: true).CountAsync());

        // Whitespace in the label collapses for an exact match too.
        CollectionAssert.AreEqual(new[] { "c" },
            await IdsAsync(page.GetByLabel("Confirm email address", exact: true)));
    }

    // --- GetByText ---

    [TestMethod]
    public async Task GetByText_MatchesTheInnermostElement()
    {
        var page = await ContentAsync("""
            <title>Dashboard</title>
            <div id=outer><section id=mid><h1 id=inner>Dashboard</h1></section></div>
            <p id=split>Hello <b id=bold>world</b></p>
            <input id=submit type=submit value="Dashboard">
            <script>var s = "Dashboard";</script>
            """);

        CollectionAssert.AreEqual(new[] { "inner", "submit" }, await IdsAsync(page.GetByText("Dashboard")));

        // Text that spans child elements matches the element holding all of it.
        CollectionAssert.AreEqual(new[] { "split" }, await IdsAsync(page.GetByText("Hello world")));
        CollectionAssert.AreEqual(new[] { "bold" }, await IdsAsync(page.GetByText("world")));
    }

    [TestMethod]
    public async Task GetByText_ExactFalse_IsCaseInsensitiveSubstring_ExactTrue_IsWholeString()
    {
        var page = await ContentAsync("""
            <span id=a>Sign in</span>
            <span id=b>Sign in with   SSO</span>
            <span id=c>  sign IN  </span>
            """);

        CollectionAssert.AreEqual(new[] { "a", "b", "c" }, await IdsAsync(page.GetByText("sign in")));
        CollectionAssert.AreEqual(new[] { "b" }, await IdsAsync(page.GetByText("in with sso")));

        CollectionAssert.AreEqual(new[] { "a" }, await IdsAsync(page.GetByText("Sign in", exact: true)));
        CollectionAssert.AreEqual(new[] { "b" }, await IdsAsync(page.GetByText("Sign in with SSO", exact: true)));
        CollectionAssert.AreEqual(new[] { "c" }, await IdsAsync(page.GetByText("sign IN", exact: true)));
    }

    [TestMethod]
    public async Task GetByText_ReachesIntoOpenShadowRoots()
    {
        var page = await ContentAsync("""
            <div id=host></div>
            <script>
              document.getElementById('host').attachShadow({mode:'open'}).innerHTML =
                '<label for=s>Search</label><input id=s><span id=t>Inside</span>';
            </script>
            """);

        Assert.AreEqual("t", await page.GetByText("Inside").GetAttributeAsync("id"));
        Assert.AreEqual("s", await page.GetByLabel("Search").GetAttributeAsync("id"));
    }

    // --- Attribute factories ---

    [TestMethod]
    public async Task AttributeFactories_HonorExact()
    {
        var page = await ContentAsync("""
            <input id=p1 placeholder="Search products">
            <input id=p2 placeholder="search">
            <span id=t1 title="Close dialog">x</span>
            <span id=t2 title="Close">x</span>
            <img id=a1 alt="Company logo" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
            <img id=a2 alt="Logo" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
            """);

        CollectionAssert.AreEqual(new[] { "p1", "p2" }, await IdsAsync(page.GetByPlaceholder("SEARCH")));
        CollectionAssert.AreEqual(new[] { "p2" }, await IdsAsync(page.GetByPlaceholder("search", exact: true)));

        CollectionAssert.AreEqual(new[] { "t1", "t2" }, await IdsAsync(page.GetByTitle("close")));
        CollectionAssert.AreEqual(new[] { "t2" }, await IdsAsync(page.GetByTitle("Close", exact: true)));

        CollectionAssert.AreEqual(new[] { "a1", "a2" }, await IdsAsync(page.GetByAltText("logo")));
        CollectionAssert.AreEqual(new[] { "a2" }, await IdsAsync(page.GetByAltText("Logo", exact: true)));
    }

    // --- Escaping ---

    /// <summary>
    /// Quotes, brackets and backslashes in the text a factory is given end up in the selector it
    /// builds, and none of them may end that selector early or change what it means.
    /// </summary>
    [TestMethod]
    public async Task Factories_TakeTextWithQuotesBracketsAndBackslashes()
    {
        const string Awkward = """Say "hi" [now] \ 'ok'""";

        var page = await ContentAsync("""
            <button id=b>Say "hi" [now] \ 'ok'</button>
            <label id=l for=i>Say "hi" [now] \ 'ok'</label><input id=i placeholder='Say "hi" [now] \ &#39;ok&#39;' title='Say "hi" [now] \ &#39;ok&#39;'>
            <span id=s data-testid='Say "hi" [now] \ &#39;ok&#39;'>x</span>
            """);

        Assert.AreEqual("b", await page.GetByRole("button", Awkward).GetAttributeAsync("id"));
        Assert.AreEqual("i", await page.GetByLabel(Awkward, exact: true).GetAttributeAsync("id"));
        Assert.AreEqual("i", await page.GetByPlaceholder(Awkward, exact: true).GetAttributeAsync("id"));
        Assert.AreEqual("i", await page.GetByTitle(Awkward).GetAttributeAsync("id"));
        Assert.AreEqual("s", await page.GetByTestId(Awkward).GetAttributeAsync("id"));
        CollectionAssert.AreEqual(new[] { "b", "l" }, await IdsAsync(page.GetByText(Awkward, exact: true)));
    }

    [TestMethod]
    public async Task GetByRole_NameWithAQuote()
    {
        var page = await ContentAsync("""<button id=b aria-label='Open "Q3" report'>open</button>""");

        Assert.AreEqual("b", await page.GetByRole("button", "Open \"Q3\" report").GetAttributeAsync("id"));
    }

    // --- Actions resolve through the same matching ---

    [TestMethod]
    public async Task ActionsReachTheElementTheFactoryMatched()
    {
        var page = await ContentAsync("""
            <form onsubmit="event.preventDefault()">
              <label>Email <input id=e></label>
              <button type=button id=save onclick="this.textContent='Saved'">Save</button>
            </form>
            """);

        await page.GetByLabel("Email").FillAsync("a@b.c");
        await page.GetByRole("button", "Save").ClickAsync();

        Assert.AreEqual("a@b.c", await page.EvaluateAsync<string>("document.getElementById('e').value"));
        Assert.AreEqual("Saved", await page.EvaluateAsync<string>("document.getElementById('save').textContent"));
    }
}
