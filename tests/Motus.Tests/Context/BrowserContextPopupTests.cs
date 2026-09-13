using Motus.Abstractions;

namespace Motus.Tests.Context;

/// <summary>
/// Pins that a tab a page opens for itself is a page of the context that page belongs to, rather
/// than a window nothing in the API can reach.
/// </summary>
/// <remarks>
/// A browser Motus starts is driven through contexts Motus created, and those contexts used to
/// hold only the pages they were asked for. A link with <c>target="_blank"</c> or a call to
/// <c>window.open</c> therefore loaded a document that no caller could see, address, or close.
///
/// The opener is navigated to a <c>data:</c> URL and the tab it opens is <c>about:blank</c>,
/// because none of what is measured here depends on the document: the question is whether the tab
/// arrives at all, which page is told about it, and what it is given when it does.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class BrowserContextPopupTests
{
    private const string OpenerDocument = "data:text/html,<h1>opener</h1>";

    private static readonly ViewportSize ContextViewport = new(640, 480);

    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _opener;

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

        _context = await _browser.NewContextAsync(new ContextOptions { Viewport = ContextViewport });
        _opener = await _context.NewPageAsync();
        await _opener.GotoAsync(OpenerDocument);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    /// <summary>
    /// Opens a tab from the page and reports whether the browser accepted the request, so a test
    /// that goes on to wait for the tab is not waiting for something that was never opened.
    /// </summary>
    private async Task OpenTabAsync(IPage page)
    {
        var opened = await page.EvaluateAsync<string>(
            "window.open('about:blank', '_blank') ? 'opened' : 'blocked'");

        Assert.AreEqual("opened", opened, "The browser refused to open the tab.");
    }

    private static async Task WaitForPagesAsync(IBrowserContext context, int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (context.Pages.Count >= count)
                return;

            await Task.Delay(50);
        }

        Assert.Fail($"The context holds {context.Pages.Count} page(s); {count} were expected.");
    }

    [TestMethod]
    public async Task ATabThePageOpens_BecomesAPageOfTheContext()
    {
        await OpenTabAsync(_opener!);
        await WaitForPagesAsync(_context!, 2);

        Assert.AreEqual(2, _context!.Pages.Count);
        CollectionAssert.Contains(_context.Pages.ToArray(), _opener, "The opener should still be listed.");
    }

    /// <summary>
    /// The browser's own first window is not the caller's, and a browser Motus started must not
    /// grow a context nobody asked for just because it is now watching what opens.
    /// </summary>
    [TestMethod]
    public async Task ATabThePageOpens_DoesNotAddAContext()
    {
        await OpenTabAsync(_opener!);
        await WaitForPagesAsync(_context!, 2);

        Assert.AreEqual(1, _browser!.Contexts.Count,
            "Only the context that was asked for should be listed.");
    }

    [TestMethod]
    public async Task ATabThePageOpens_IsAnnouncedToThePageThatOpenedIt()
    {
        var announced = new TaskCompletionSource<IPage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _opener!.Popup += (_, popup) => announced.TrySetResult(popup);

        await OpenTabAsync(_opener);

        var completed = await Task.WhenAny(announced.Task, Task.Delay(TimeSpan.FromSeconds(15)));
        Assert.AreSame(announced.Task, completed, "The opener was never told about the tab it opened.");

        var page = await announced.Task;
        Assert.AreNotSame(_opener, page, "The opener was handed itself.");
        CollectionAssert.Contains(_context!.Pages.ToArray(), page,
            "The page handed over should be one of the context's pages.");
    }

    /// <summary>
    /// A tab that appears on its own is expected to look like the tabs the context opened itself,
    /// so the context's emulation reaches it rather than leaving one tab at the browser default.
    /// </summary>
    [TestMethod]
    public async Task ATabThePageOpens_IsGivenTheContextsOptions()
    {
        await OpenTabAsync(_opener!);
        await WaitForPagesAsync(_context!, 2);

        var opened = _context!.Pages.Single(p => p != _opener);
        Assert.AreEqual(ContextViewport, opened.ViewportSize);
    }

    [TestMethod]
    public async Task ClosingATabThePageOpened_RemovesItFromTheContext()
    {
        await OpenTabAsync(_opener!);
        await WaitForPagesAsync(_context!, 2);

        var opened = _context!.Pages.Single(p => p != _opener);
        await opened.CloseAsync();

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline && _context.Pages.Count > 1)
            await Task.Delay(50);

        Assert.AreEqual(1, _context.Pages.Count);
        Assert.AreSame(_opener, _context.Pages[0]);
    }

    /// <summary>
    /// A page opened by a call and a page opened by the page arrive by different routes that both
    /// end in the same list, and the browser announces the first of them to the second's route as
    /// well. Asking for a page while a tab is opening must still produce one page per tab.
    /// </summary>
    [TestMethod]
    public async Task AskingForAPageWhileATabOpens_WrapsEachTabOnce()
    {
        var requested = _context!.NewPageAsync();
        var opening = OpenTabAsync(_opener!);

        await Task.WhenAll(requested, opening);
        await WaitForPagesAsync(_context, 3);

        // Long enough for a second wrapping of either target to have landed if one were coming.
        await Task.Delay(1000);

        var pages = _context.Pages;
        Assert.AreEqual(3, pages.Count, "Each tab should be one page: the opener, the tab it opened, and the one asked for.");
        Assert.AreEqual(3, pages.Distinct().Count(), "The same page was listed twice.");

        var urls = pages.Select(p => p.Url).ToList();
        Assert.AreEqual(1, urls.Count(u => u == OpenerDocument), "The opener should be listed once.");
    }
}
