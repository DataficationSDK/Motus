using System.Diagnostics;
using Motus.Abstractions;
using Motus.Assertions;

namespace Motus.Tests.Locator;

/// <summary>
/// Pins that locators work on Firefox, where every command reaches the browser through the
/// WebDriver BiDi translation rather than CDP.
/// </summary>
/// <remarks>
/// A locator turns its matches into element handles and then drives each one through script calls
/// that name the element, so these cover every selector strategy that runs in the page, the
/// chained and parent queries that start from an element, the actions that scroll and confirm what
/// they did, and the assertions that wait for an element to appear. <c>GetByRole</c> reads the
/// accessibility tree, which WebDriver BiDi does not expose, and is pinned to fail with a clear
/// message instead.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
[TestCategory("Firefox")]
public class FirefoxLocatorIntegrationTests
{
    private static IBrowser? s_browser;
    private IBrowserContext? _context;
    private IPage? _page;

    [ClassInitialize]
    public static async Task ClassSetup(TestContext _)
    {
        try
        {
            BrowserFinder.Resolve(BrowserChannel.Firefox, executablePath: null);
        }
        catch (FileNotFoundException)
        {
            return;
        }

        s_browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });
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
            Assert.Inconclusive("Firefox not found on this machine.");
            return;
        }

        _context = await s_browser.NewContextAsync();
        _page = await _context.NewPageAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_context is not null)
            await _context.CloseAsync();
    }

    private async Task<IPage> ContentAsync(string html)
    {
        await _page!.SetContentAsync(html);
        return _page;
    }

    /// <summary>
    /// The repro from the issue: a CSS locator that clicks a button.
    /// </summary>
    [TestMethod]
    public async Task Click_CssLocator_ClicksTheElement()
    {
        var page = await ContentAsync(
            "<button onclick=\"document.body.dataset.clicked='yes'\">Save</button>");

        await page.Locator("button").ClickAsync(timeout: 10_000);

        Assert.AreEqual("yes", await page.EvaluateAsync<string>("document.body.dataset.clicked"));
    }

    /// <summary>
    /// The main frame's locators are measured like the page's, so a click through one needs
    /// nothing Chromium-only either.
    /// </summary>
    [TestMethod]
    public async Task Click_MainFrameLocator_ClicksTheElement()
    {
        var page = await ContentAsync(
            "<div style='height:5000px'></div>"
            + "<button onclick=\"document.body.dataset.clicked='yes'\">Save</button>");

        await page.MainFrame.GetByText("Save").ClickAsync(timeout: 10_000);

        Assert.AreEqual("yes", await page.EvaluateAsync<string>("document.body.dataset.clicked"));
        Assert.IsNotNull(await page.MainFrame.Locator("button").BoundingBoxAsync());
    }

    [TestMethod]
    public async Task Fill_ThenRead_RoundTripsTheValue()
    {
        var page = await ContentAsync("<input id=name>");

        await page.Locator("#name").FillAsync("Ada", timeout: 10_000);

        Assert.AreEqual("Ada", await page.Locator("#name").InputValueAsync());
        Assert.AreEqual("Ada", await page.EvaluateAsync<string>("document.getElementById('name').value"));
    }

    /// <summary>
    /// Every strategy that runs a query in the page finds the element it was written for.
    /// </summary>
    [TestMethod]
    public async Task EveryInPageStrategy_FindsItsElement()
    {
        var page = await ContentAsync(
            "<p id=greeting>Hello there</p>"
            + "<label for=email>Email</label><input id=email placeholder='you@example.com'>"
            + "<span id=tagged data-testid=status>ok</span>"
            + "<img id=logo alt='Company logo' title='Logo' src='data:,'>");

        Assert.AreEqual("greeting", await page.GetByText("hello there").GetAttributeAsync("id"));
        Assert.AreEqual("email", await page.GetByLabel("Email").GetAttributeAsync("id"));
        Assert.AreEqual("email", await page.GetByPlaceholder("you@").GetAttributeAsync("id"));
        Assert.AreEqual("tagged", await page.GetByTestId("status").GetAttributeAsync("id"));
        Assert.AreEqual("logo", await page.GetByAltText("Company logo").GetAttributeAsync("id"));
        Assert.AreEqual("logo", await page.GetByTitle("Logo").GetAttributeAsync("id"));
        Assert.AreEqual("greeting", await page.Locator("xpath=//p").GetAttributeAsync("id"));
        Assert.AreEqual("greeting", await page.Locator("text=Hello there").Last.GetAttributeAsync("id"));
        Assert.AreEqual(1, await page.Locator("css=#tagged").CountAsync());
    }

    /// <summary>
    /// A chained locator queries under each element the first part matched, and a parent step walks
    /// up from it. Both start from an element rather than the document, and two matches that share a
    /// parent count it once.
    /// </summary>
    [TestMethod]
    public async Task ChainedAndParentLocators_QueryFromTheElement()
    {
        var page = await ContentAsync(
            "<ul id=a><li class=item>One</li><li class=item>Two</li></ul>"
            + "<ul id=b><li class=item>Three</li></ul>");

        var underA = page.Locator("#a").Locator(".item");
        Assert.AreEqual(2, await underA.CountAsync());
        CollectionAssert.AreEqual(new[] { "One", "Two" }, (await underA.AllTextContentsAsync()).ToArray());

        Assert.AreEqual(2, await page.Locator(".item").Locator("..").CountAsync());
        Assert.AreEqual("b", await page.GetByText("Three").Locator("..").GetAttributeAsync("id"));
        Assert.AreEqual(2, await page.Locator("ul").First.Locator("li").CountAsync());
    }

    /// <summary>
    /// The rest of the actions and queries, each through the same element reference.
    /// </summary>
    /// <remarks>
    /// A double-click is left out: Firefox counts the two clicks it is made of as two single
    /// clicks, which is about how mouse input is translated rather than how the element is found.
    /// </remarks>
    [TestMethod]
    public async Task OtherActionsAndQueries_ReachTheElement()
    {
        var page = await ContentAsync(
            "<button id=b onmouseover=\"document.body.dataset.hover='yes'\""
            + " onpointerdown=\"document.body.dataset.touch=event.pointerType\">Go <b>now</b></button>"
            + "<input id=t value=old onkeydown=\"if (event.key === 'Enter') document.body.dataset.enter='yes'\">"
            + "<input id=off disabled>");

        var button = page.Locator("#b");
        await button.HoverAsync(timeout: 10_000);
        await button.TapAsync(timeout: 10_000);
        Assert.AreEqual("yes", await page.EvaluateAsync<string>("document.body.dataset.hover"), "hover");
        Assert.AreEqual("touch", await page.EvaluateAsync<string>("document.body.dataset.touch"), "tap");

        Assert.AreEqual("Go now", await button.InnerTextAsync());
        Assert.AreEqual("Go <b>now</b>", await button.InnerHTMLAsync());
        Assert.IsTrue(await button.IsVisibleAsync());
        Assert.IsTrue(await button.IsEnabledAsync());
        Assert.IsFalse(await page.Locator("#off").IsEnabledAsync());
        var box = await button.BoundingBoxAsync();
        Assert.IsNotNull(box);
        Assert.IsTrue(box.Width > 0 && box.Height > 0);

        var field = page.Locator("#t");
        await field.ClearAsync(timeout: 10_000);
        await field.TypeAsync("new");
        await field.PressAsync("Enter");
        Assert.AreEqual("new", await field.InputValueAsync());
        Assert.AreEqual("yes", await page.EvaluateAsync<string>("document.body.dataset.enter"), "press");
        Assert.IsTrue(await field.IsEditableAsync());

        var handle = await field.ElementHandleAsync();
        Assert.AreEqual("new", await handle.EvaluateAsync<string>("function() { return this.value; }"));
    }

    /// <summary>
    /// A pointer action scrolls a target below the fold into view before it clicks.
    /// </summary>
    [TestMethod]
    public async Task Click_ScrollsATargetBelowTheFoldIntoView()
    {
        // BiDi has no viewport emulation, so the spacer is taller than any default window instead.
        var page = await ContentAsync(
            "<div style='height:5000px'></div>"
            + "<button onclick=\"document.body.dataset.clicked='yes'\">Far</button>");

        await page.Locator("button").ClickAsync(timeout: 10_000);

        Assert.AreEqual("yes", await page.EvaluateAsync<string>("document.body.dataset.clicked"));
        Assert.IsTrue(await page.EvaluateAsync<double>("window.scrollY") > 0, "The page did not scroll.");
    }

    [TestMethod]
    public async Task CheckAndUncheck_ConfirmTheState()
    {
        var page = await ContentAsync("<input type=checkbox id=agree><label for=agree>Agree</label>");
        var box = page.Locator("#agree");

        await box.CheckAsync(timeout: 10_000);
        Assert.IsTrue(await box.IsCheckedAsync());

        // Already checked: nothing to click, and still checked afterwards.
        await box.CheckAsync(timeout: 10_000);
        Assert.IsTrue(await box.IsCheckedAsync());

        await box.UncheckAsync(timeout: 10_000);
        Assert.IsFalse(await box.IsCheckedAsync());
    }

    /// <summary>
    /// The state is read back after the click, so a click a handler cancels is reported rather than
    /// passing as though it had checked the box.
    /// </summary>
    [TestMethod]
    public async Task Check_ACancelledClick_Throws()
    {
        var page = await ContentAsync("<input type=checkbox id=stuck onclick='event.preventDefault()'>");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(
            () => page.Locator("#stuck").CheckAsync(timeout: 10_000));

        StringAssert.Contains(ex.Message, "still unchecked");
    }

    /// <summary>
    /// An assertion made before the element renders waits for it, and an assertion about an element
    /// that never renders fails at its timeout reporting the element missing.
    /// </summary>
    [TestMethod]
    public async Task Assertions_WaitForAMissingElement()
    {
        var page = await ContentAsync(
            "<p>start</p><script>setTimeout(() => {"
            + "const h = document.createElement('h1'); h.id = 'done'; h.textContent = 'Saved';"
            + "document.body.append(h); }, 1000)</script>");

        Assert.AreEqual(0, await page.Locator("#done").CountAsync(),
            "The element rendered before the assertion started.");

        var options = new AssertionOptions { Timeout = 10_000 };
        await Expect.That(page.Locator("#done")).ToBeVisibleAsync(options);
        await Expect.That(page.Locator("#done")).ToHaveTextAsync("Saved", options);

        var elapsed = Stopwatch.StartNew();
        await Expect.That(page.Locator("#nope")).ToBeHiddenAsync(options);
        Assert.IsTrue(elapsed.ElapsedMilliseconds < 2_000,
            $"ToBeHidden on a missing element took {elapsed.ElapsedMilliseconds}ms.");

        var ex = await Assert.ThrowsExceptionAsync<MotusAssertionException>(
            () => Expect.That(page.Locator("#nope"))
                .ToHaveTextAsync("Saved", new AssertionOptions { Timeout = 1_500 }));
        Assert.AreEqual("<element not found>", ex.Actual);
    }

    /// <summary>
    /// The element itself can be handed to a script as an argument, and arrays and objects reach
    /// the page as arrays and objects.
    /// </summary>
    [TestMethod]
    public async Task ElementAndStructuredArguments_ReachThePage()
    {
        var page = await ContentAsync(
            "<select id=s multiple><option value=a>A</option><option value=b>B</option><option value=c>C</option></select>"
            + "<div id=d></div>");

        var selected = await page.Locator("#s").SelectOptionAsync(["a", "c"]);
        CollectionAssert.AreEqual(new[] { "a", "c" }, selected.ToArray());

        var tag = await page.Locator("#d").EvaluateWithElementAsync<string>(
            "(el, arg) => el.tagName + ':' + arg.label + ':' + arg.items.length", new { label = "x", items = new[] { 1, 2 } });
        Assert.AreEqual("DIV:x:2", tag);

        await page.EvaluateAsync<object?>(
            "document.getElementById('d').addEventListener('ping', e => document.body.dataset.ping = String(e.bubbles))");
        await page.Locator("#d").DispatchEventAsync("ping", new { bubbles = true });
        Assert.AreEqual("true", await page.EvaluateAsync<string>("document.body.dataset.ping"));
    }

    /// <summary>
    /// Files are set on the input a locator found, by the same reference every other command uses
    /// for it.
    /// </summary>
    [TestMethod]
    public async Task SetInputFiles_SetsTheFilesOnTheInput()
    {
        var page = await ContentAsync("<input type=file id=f multiple>");

        await page.Locator("#f").SetInputFilesAsync(
        [
            new FilePayload("a.txt", "text/plain", "hello"u8.ToArray()),
            new FilePayload("b.txt", "text/plain", "hi"u8.ToArray()),
        ], timeout: 10_000);

        Assert.AreEqual("a.txt,b.txt", await page.EvaluateAsync<string>(
            "Array.from(document.getElementById('f').files).map(f => f.name).join(',')"));
        Assert.AreEqual(5, await page.EvaluateAsync<int>("document.getElementById('f').files[0].size"));
    }

    /// <summary>
    /// <c>GetByRole</c> reads the accessibility tree, which only Chromium exposes. On Firefox it
    /// fails at once and says so, rather than timing out or matching nothing.
    /// </summary>
    [TestMethod]
    public async Task GetByRole_FailsWithAClearError()
    {
        var page = await ContentAsync("<button>Save</button>");

        var elapsed = Stopwatch.StartNew();
        var ex = await Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => page.GetByRole("button", "Save").ClickAsync(timeout: 10_000));

        Assert.IsTrue(elapsed.ElapsedMilliseconds < 5_000, $"Failed after {elapsed.ElapsedMilliseconds}ms.");
        StringAssert.Contains(ex.Message, "GetByRole");
        StringAssert.Contains(ex.Message, "Chromium");
    }

    [TestMethod]
    public async Task AccessibilityAssertions_FailWithAClearError()
    {
        var page = await ContentAsync("<button>Save</button>");

        var ex = await Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => Expect.That(page.Locator("button")).ToHaveRoleAsync("button", new AssertionOptions { Timeout = 10_000 }));

        StringAssert.Contains(ex.Message, "ToHaveRoleAsync");
        StringAssert.Contains(ex.Message, "Chromium");
    }
}
