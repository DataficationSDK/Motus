using Motus.Abstractions;

namespace Motus.Tests.Locator;

/// <summary>
/// Pins that a pointer action reaches an element the viewport does not show, by scrolling it into
/// view first, and that check and uncheck report a click that changed nothing.
/// </summary>
/// <remarks>
/// Every fixture puts its target below a spacer taller than the 800x600 viewport, the shape of a
/// long page with something worth clicking near the bottom. Results are read back through
/// <see cref="IPage.EvaluateAsync{T}"/>, so a test passes only when the page itself saw the action.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class ScrollIntoViewTests
{
    private IBrowser? _browser;
    private IPage _page = null!;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
            return;
        }

        var context = await _browser.NewContextAsync(new ContextOptions { Viewport = new ViewportSize(800, 600) });
        _page = await context.NewPageAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    // The fixture from the report: a button and a checkbox below a 3000px spacer.
    private const string BelowTheFold =
        "data:text/html,<div style='height:3000px'></div>"
        + "<button id=b onclick=\"this.textContent='clicked'\" ondblclick=\"this.dataset.dbl='yes'\""
        + " onmouseenter=\"this.dataset.hovered='yes'\">Go</button>"
        + "<input type=checkbox id=c>";

    private Task<double> ScrollYAsync() => _page.EvaluateAsync<double>("window.scrollY");

    [TestMethod]
    public async Task Click_BelowTheFold_ScrollsToTheElementAndClicksIt()
    {
        await _page.GotoAsync(BelowTheFold);

        await _page.Locator("#b").ClickAsync(3000);

        Assert.AreEqual("clicked", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
        Assert.IsTrue(await ScrollYAsync() > 0, "The page was never scrolled.");
    }

    [TestMethod]
    public async Task ClickWithOptions_BelowTheFold_ScrollsToTheElementAndClicksIt()
    {
        await _page.GotoAsync(
            "data:text/html,<div style='height:3000px'></div>"
            + "<button id=b oncontextmenu=\"event.preventDefault(); this.textContent='menu'\">Go</button>");

        await _page.Locator("#b").ClickAsync(new MouseButtonOptions(MouseButton.Right), 3000);

        Assert.AreEqual("menu", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
    }

    [TestMethod]
    public async Task DblClick_BelowTheFold_ScrollsToTheElementAndDoubleClicksIt()
    {
        await _page.GotoAsync(BelowTheFold);

        await _page.Locator("#b").DblClickAsync(3000);

        Assert.AreEqual("yes", await _page.EvaluateAsync<string>("document.getElementById('b').dataset.dbl"));
    }

    [TestMethod]
    public async Task Hover_BelowTheFold_ScrollsToTheElementAndHoversIt()
    {
        await _page.GotoAsync(BelowTheFold);

        await _page.Locator("#b").HoverAsync(3000);

        Assert.AreEqual("yes", await _page.EvaluateAsync<string>("document.getElementById('b').dataset.hovered"));
    }

    [TestMethod]
    public async Task Check_BelowTheFold_ChecksTheBox()
    {
        await _page.GotoAsync(BelowTheFold);

        await _page.Locator("#c").CheckAsync(3000);

        Assert.IsTrue(await _page.EvaluateAsync<bool>("document.getElementById('c').checked"));
    }

    [TestMethod]
    public async Task Uncheck_BelowTheFold_UnchecksTheBox()
    {
        await _page.GotoAsync(BelowTheFold);
        await _page.EvaluateAsync<bool>("document.getElementById('c').checked = true");

        await _page.Locator("#c").UncheckAsync(3000);

        Assert.IsFalse(await _page.EvaluateAsync<bool>("document.getElementById('c').checked"));
    }

    [TestMethod]
    public async Task SetChecked_BelowTheFold_SetsTheState()
    {
        await _page.GotoAsync(BelowTheFold);

        await _page.Locator("#c").SetCheckedAsync(true, 3000);

        Assert.IsTrue(await _page.EvaluateAsync<bool>("document.getElementById('c').checked"));
    }

    [TestMethod]
    public async Task Click_InsideAScrollableContainer_ScrollsTheContainer()
    {
        await _page.GotoAsync(
            "data:text/html,<div id=box style='height:200px;overflow:auto'>"
            + "<div style='height:1000px'></div>"
            + "<button id=b onclick=\"this.textContent='clicked'\">Go</button></div>");

        await _page.Locator("#b").ClickAsync(3000);

        Assert.AreEqual("clicked", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
        Assert.IsTrue(await _page.EvaluateAsync<double>("document.getElementById('box').scrollTop") > 0,
            "The container was never scrolled.");
    }

    [TestMethod]
    public async Task Click_InsideAContainerBelowTheFold_ScrollsBothThePageAndTheContainer()
    {
        await _page.GotoAsync(
            "data:text/html,<div style='height:3000px'></div>"
            + "<div id=box style='height:200px;overflow:auto'>"
            + "<div style='height:1000px'></div>"
            + "<button id=b onclick=\"this.textContent='clicked'\">Go</button></div>");

        await _page.Locator("#b").ClickAsync(3000);

        Assert.AreEqual("clicked", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
    }

    [TestMethod]
    public async Task Click_InsideAFrameBelowTheFold_ScrollsThePageToTheFrame()
    {
        await _page.GotoAsync(
            "data:text/html,<div style='height:3000px'></div>"
            + "<iframe id=f srcdoc=\"<button id=b onclick='window.clicks=(window.clicks||0)+1'>Go</button>\"></iframe>");

        var frame = await WaitForChildFrameAsync();

        await frame.Locator("#b").ClickAsync(3000);

        Assert.AreEqual(1, await frame.EvaluateAsync<int>("window.clicks || 0"));
        Assert.IsTrue(await ScrollYAsync() > 0, "The page was never scrolled to the frame.");
    }

    [TestMethod]
    public async Task Click_WithSmoothScrollBehavior_StillLandsOnTheElement()
    {
        await _page.GotoAsync(
            "data:text/html,<style>html{scroll-behavior:smooth}</style><div style='height:3000px'></div>"
            + "<button id=b onclick=\"this.textContent='clicked'\">Go</button>");

        await _page.Locator("#b").ClickAsync(3000);

        Assert.AreEqual("clicked", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
    }

    [TestMethod]
    public async Task Click_AlreadyInView_LeavesThePageWhereItIs()
    {
        await _page.GotoAsync(
            "data:text/html,<div style='height:200px'></div>"
            + "<button id=b onclick=\"this.textContent='clicked'\">Go</button>"
            + "<div style='height:3000px'></div>");
        await _page.EvaluateAsync<double>("window.scrollTo(0, 100), window.scrollY");

        await _page.Locator("#b").ClickAsync(3000);

        Assert.AreEqual("clicked", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
        Assert.AreEqual(100, await ScrollYAsync(), "An element already in view was scrolled anyway.");
    }

    [TestMethod]
    public async Task ScrollIntoViewIfNeeded_AlreadyInView_LeavesThePageWhereItIs()
    {
        await _page.GotoAsync(
            "data:text/html,<div style='height:200px'></div>"
            + "<button id=b>Go</button>"
            + "<div style='height:3000px'></div>");
        await _page.EvaluateAsync<double>("window.scrollTo(0, 100), window.scrollY");

        await _page.Locator("#b").ScrollIntoViewIfNeededAsync(3000);

        Assert.AreEqual(100, await ScrollYAsync());
    }

    [TestMethod]
    public async Task ScrollIntoViewIfNeeded_BelowTheFold_BringsTheElementIntoTheViewport()
    {
        await _page.GotoAsync(BelowTheFold);

        await _page.Locator("#b").ScrollIntoViewIfNeededAsync(3000);

        var top = await _page.EvaluateAsync<double>("document.getElementById('b').getBoundingClientRect().top");
        Assert.IsTrue(top >= 0 && top < 600, $"The element's top is at {top}, outside the 600px viewport.");
    }

    [TestMethod]
    public async Task Click_ElementThatMovesOutOfViewWhileWaiting_IsScrolledToAgain()
    {
        // The button is in view but disabled. When it is enabled, a spacer is inserted above it in
        // the same task, so the scroll has to happen on the pass that finds it enabled rather than
        // once at the start of the wait.
        await _page.GotoAsync(
            "data:text/html,<div id=top></div><button id=b disabled onclick=\"this.textContent='clicked'\">Go</button>"
            + "<script>setTimeout(function(){document.getElementById('top').style.height='3000px';"
            + "document.getElementById('b').disabled=false;},300)</script>");

        await _page.Locator("#b").ClickAsync(5000);

        Assert.AreEqual("clicked", await _page.EvaluateAsync<string>("document.getElementById('b').textContent"));
    }

    // --- check and uncheck confirm the state changed ---

    [TestMethod]
    public async Task Check_WhenTheClickIsCancelled_Throws()
    {
        await _page.GotoAsync("data:text/html,<input type=checkbox id=c onclick='event.preventDefault()'>");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => _page.Locator("#c").CheckAsync(3000));

        StringAssert.Contains(ex.Message, "still unchecked");
    }

    [TestMethod]
    public async Task Uncheck_WhenTheClickIsCancelled_Throws()
    {
        await _page.GotoAsync("data:text/html,<input type=checkbox id=c checked onclick='event.preventDefault()'>");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => _page.Locator("#c").UncheckAsync(3000));

        StringAssert.Contains(ex.Message, "still checked");
    }

    [TestMethod]
    public async Task Uncheck_OnACheckedRadioButton_Throws()
    {
        await _page.GotoAsync("data:text/html,<input type=radio name=r id=r checked>");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => _page.Locator("#r").UncheckAsync(3000));

        StringAssert.Contains(ex.Message, "radio button");
        Assert.IsTrue(await _page.EvaluateAsync<bool>("document.getElementById('r').checked"));
    }

    [TestMethod]
    public async Task Check_OnAnElementWithNoCheckedState_Throws()
    {
        await _page.GotoAsync("data:text/html,<button id=b>Go</button>");

        var ex = await Assert.ThrowsExceptionAsync<MotusException>(() => _page.Locator("#b").CheckAsync(3000));

        StringAssert.Contains(ex.Message, "not a checkbox");
    }

    [TestMethod]
    public async Task Check_OnAnAlreadyCheckedBox_DoesNotClick()
    {
        await _page.GotoAsync(
            "data:text/html,<input type=checkbox id=c checked onclick='window.clicks=(window.clicks||0)+1'>");

        await _page.Locator("#c").CheckAsync(3000);

        Assert.AreEqual(0, await _page.EvaluateAsync<int>("window.clicks || 0"));
        Assert.IsTrue(await _page.EvaluateAsync<bool>("document.getElementById('c').checked"));
    }

    [TestMethod]
    public async Task Check_OnAnAriaCheckbox_ReadsAriaChecked()
    {
        await _page.GotoAsync(
            "data:text/html,<div id=c role=checkbox aria-checked=false tabindex=0 style='width:20px;height:20px'"
            + " onclick=\"this.setAttribute('aria-checked', this.getAttribute('aria-checked') === 'true' ? 'false' : 'true')\"></div>");

        await _page.Locator("#c").CheckAsync(3000);
        Assert.AreEqual("true", await _page.EvaluateAsync<string>("document.getElementById('c').getAttribute('aria-checked')"));

        // Already checked, so a second check must not click it back off.
        await _page.Locator("#c").CheckAsync(3000);
        Assert.AreEqual("true", await _page.EvaluateAsync<string>("document.getElementById('c').getAttribute('aria-checked')"));
    }

    [TestMethod]
    public async Task Check_OnALabel_ChecksTheControlItLabels()
    {
        await _page.GotoAsync("data:text/html,<label id=l for=c>Remember me</label><input type=checkbox id=c>");

        await _page.Locator("#l").CheckAsync(3000);

        Assert.IsTrue(await _page.EvaluateAsync<bool>("document.getElementById('c').checked"));
    }

    private async Task<IFrame> WaitForChildFrameAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var frame = _page.Frames.FirstOrDefault(f => f != _page.MainFrame);
            if (frame is not null)
            {
                try
                {
                    await frame.EvaluateAsync<int>("1");
                    return frame;
                }
                catch (InvalidOperationException)
                {
                    // Context not published yet.
                }
            }

            await Task.Delay(100);
        }

        Assert.Inconclusive("The fixture's frame never became addressable.");
        return null!;
    }
}
