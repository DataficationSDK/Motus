using Motus.Abstractions;

namespace Motus.Tests.Selectors;

/// <summary>
/// Pins that a <c>role=</c> selector resolves from the document of whatever it was built on: the
/// page, one frame, or a parent locator it is chained under.
/// </summary>
/// <remarks>
/// The accessibility-tree strategy is the only one that scopes a protocol command by node instead
/// of running JavaScript in an execution context, so a page-level locator used to reach the
/// browser with no root node named and fail the call outright. Resolution is read back through
/// text and attribute values so a query landing in the wrong document gives a wrong answer rather
/// than an accidentally right one. The frame is a <c>srcdoc</c> frame, which renders in the page's
/// own process and so needs no server to stand up an origin.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class RoleSelectorScopeTests
{
    private IBrowser? _browser;
    private IPage? _page;

    private const string FixtureHtml = """
        data:text/html,
        <body>
          <button aria-label="Save">save-main</button>
          <div role="group" aria-label="Panel">
            <button aria-label="Panel Save" class="inner">panel-save</button>
          </div>
          <iframe id="one" srcdoc="
            <body>
              <button aria-label='Frame Save'>frame-save</button>
              <script>window.marker='frame';</script>
            </body>"></iframe>
        </body>
        """;

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

        _page = await _browser.NewPageAsync();
        await _page.GotoAsync(FixtureHtml.Replace("\n", "").Replace("\r", ""));
        await WaitForFrameAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    // The frame attaches and publishes its execution context as events after the navigation
    // settles, so the fixture waits for it to become addressable.
    private async Task<IFrame> WaitForFrameAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var frame = _page!.Frames.FirstOrDefault(f => f != _page.MainFrame);
            if (frame is not null)
            {
                try
                {
                    if (await frame.EvaluateAsync<string>("window.marker") == "frame")
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
        throw new InvalidOperationException("unreachable");
    }

    [TestMethod]
    public async Task PageLevelRoleSelector_ResolvesFromTheMainDocument()
    {
        Assert.AreEqual("save-main",
            await _page!.Locator("""role=button[name="Save"]""").TextContentAsync());

        // The bare form, with no name filter, resolves through the same root.
        Assert.AreEqual("Panel",
            await _page.Locator("role=group").GetAttributeAsync("aria-label"));

        // Rooting the query at the main document keeps it out of the frames the page hosts, which
        // is how every other strategy behaves for a page-level locator.
        await WaitForFrameAsync();
        Assert.AreEqual(0, await _page.Locator("""role=button[name="Frame Save"]""").CountAsync(),
            "A page-level role selector reached into a frame.");
    }

    [TestMethod]
    public async Task FrameRoleSelector_ResolvesInsideThatFrame()
    {
        var frame = await WaitForFrameAsync();

        Assert.AreEqual("frame-save",
            await frame.Locator("""role=button[name="Frame Save"]""").TextContentAsync());

        await Assert.ThrowsExceptionAsync<ElementNotFoundException>(
            () => frame.Locator("""role=button[name="Save"]""").TextContentAsync(null),
            "A button from the main document was matched inside the frame.");
    }

    [TestMethod]
    public async Task ChainedRoleSelector_ScopesTheChildToTheMatchedParent()
    {
        Assert.AreEqual("panel-save",
            await _page!.Locator("""role=group[name="Panel"]""").Locator("button").TextContentAsync());

        Assert.AreEqual("panel-save",
            await _page.Locator("role=group").Locator(".inner").First.TextContentAsync());
    }
}
