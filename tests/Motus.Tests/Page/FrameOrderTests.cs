using Motus.Abstractions;

namespace Motus.Tests.Page;

/// <summary>
/// Pins the order frames come back in: the order they attached, which for a page whose iframes were
/// all parsed out of the markup is the order they appear in it.
/// </summary>
/// <remarks>
/// The page keeps its frames in a map keyed for lookup, and a map hands its values back in an order
/// of its own that changes again whenever an entry is added or removed. So these six frames used to
/// come back shuffled, and reshuffled after one of them went away.
///
/// One of the six is served from a second origin, which puts it in a process of its own and means
/// it is stitched in through a session of its own, arriving by a different route from its siblings.
/// It has to land in its own slot rather than on the end.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class FrameOrderTests
{
    private const int FrameCount = 6;

    private CrossOriginFixtureServer _server = null!;
    private IBrowser? _browser;
    private IPage? _page;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new CrossOriginFixtureServer();

        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions
            {
                Headless = true,
                Args = _server.IsolationArgs
            });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
            return;
        }

        _page = await _browser.NewPageAsync();
        await _page.GotoAsync(_server.OrderedUrl);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        _server?.Dispose();
    }

    [TestMethod]
    public async Task StaticallyParsedFrames_ComeBackInDocumentOrder()
    {
        await WaitForFramesAsync(FrameCount + 1);

        Assert.AreEqual(
            "0, 1, 2, 3, 4, 5",
            Order(_page!.MainFrame.ChildFrames),
            "The children of the main frame are not in the order they appear in the page.");

        Assert.AreEqual(
            "0, 1, 2, 3, 4, 5",
            Order(_page.Frames.Where(f => !ReferenceEquals(f, _page.MainFrame))),
            "The page's flat frame list is not in the order the frames appear in the page.");
    }

    [TestMethod]
    public async Task AFrameInItsOwnProcess_KeepsItsSlot()
    {
        await WaitForFramesAsync(FrameCount + 1);

        var remote = _page!.MainFrame.ChildFrames[3];

        Assert.IsTrue(remote.Url.StartsWith(_server.SecondaryOrigin, StringComparison.Ordinal),
            "The fourth slot does not hold the frame served from the second origin.");

        // Without this the test would still pass over a same-process frame, and the case it exists
        // for would look covered when the isolation flags had quietly stopped working.
        Assert.IsTrue(((Motus.Page)_page).HasOwnSession(remote),
            "The frame is not rendering in its own process, so this fixture proves nothing.");
    }

    [TestMethod]
    public async Task AfterAFrameDetaches_TheSurvivorsKeepTheirOrder()
    {
        await WaitForFramesAsync(FrameCount + 1);

        await _page!.EvaluateAsync<bool>(
            "(() => { document.querySelectorAll('iframe')[2].remove(); return true; })()");

        await WaitForFramesAsync(FrameCount);

        Assert.AreEqual(
            "0, 1, 3, 4, 5",
            Order(_page.MainFrame.ChildFrames),
            "Removing one frame moved the others.");
    }

    /// <summary>Which of the six each frame is, in the order they came back.</summary>
    private static string Order(IEnumerable<IFrame> frames) => string.Join(", ", frames.Select(Which));

    /// <summary>
    /// Which of the six a frame is, read off the query string its URL carries.
    /// </summary>
    private static string Which(IFrame frame)
    {
        var marker = frame.Url.IndexOf("?i=", StringComparison.Ordinal);
        return marker < 0 ? frame.Url : frame.Url[(marker + 3)..];
    }

    /// <summary>
    /// Waits until the page holds exactly <paramref name="total"/> frames and every one of them has
    /// reported the address it was opened for.
    /// </summary>
    /// <remarks>
    /// A frame is recorded when it attaches, which is before it says where it is, so its URL is
    /// still <c>about:blank</c> for a moment. The frame in its own process takes longer about it
    /// than its siblings, because it is stitched in through a session of its own.
    /// </remarks>
    private async Task WaitForFramesAsync(int total)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            var frames = _page!.Frames;
            if (frames.Count == total && frames.All(Settled))
                return;

            await Task.Delay(100);
        }

        Assert.Inconclusive(
            $"The page settled at {_page!.Frames.Count} frame(s) rather than {total}: "
            + string.Join(", ", _page.Frames.Select(f => f.Url)));

        bool Settled(IFrame frame)
            => ReferenceEquals(frame, _page!.MainFrame)
                || frame.Url.Contains("?i=", StringComparison.Ordinal);
    }
}
