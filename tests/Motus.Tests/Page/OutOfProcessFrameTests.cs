using Motus.Abstractions;

namespace Motus.Tests.Page;

/// <summary>
/// Pins that a frame the browser renders in its own process behaves like any other frame.
/// </summary>
/// <remarks>
/// The fixture is served from two origins rather than written as a <c>data:</c> URL, because a
/// <c>data:</c> URL is cross-origin to nothing and so can never produce a frame in its own process.
/// Site isolation is forced through browser arguments so the test measures out-of-process behavior
/// rather than whatever Chromium currently decides about loopback hosts.
/// </remarks>
[TestClass]
[TestCategory("Integration")]
public class OutOfProcessFrameTests
{
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
        await _page.GotoAsync(_server.OuterUrl);
        await WaitForFixtureFramesAsync();
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();

        _server?.Dispose();
    }

    // The outer document holds a cross-origin frame and a sandboxed one, and the cross-origin frame
    // holds a third. All four arrive as events after the navigation settles, and the nested one only
    // after its parent's session has been armed, so the wait covers both levels.
    private async Task WaitForFixtureFramesAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            if (_page!.Frames.Count >= 4 && (await MarkersAsync()).Count >= 3)
                return;

            await Task.Delay(100);
        }

        Assert.Inconclusive(
            $"The fixture's frames never became addressable (saw {_page!.Frames.Count} frames).");
    }

    private async Task<Dictionary<string, IFrame>> MarkersAsync()
    {
        var found = new Dictionary<string, IFrame>(StringComparer.Ordinal);

        foreach (var frame in _page!.Frames)
        {
            if (frame == _page.MainFrame)
                continue;

            try
            {
                var marker = await frame.EvaluateAsync<string>("window.marker");
                if (!string.IsNullOrEmpty(marker))
                    found[marker] = frame;
            }
            catch (InvalidOperationException)
            {
                // The frame's context is not published yet.
            }
        }

        return found;
    }

    private async Task<IFrame> FrameAsync(string marker)
    {
        var markers = await MarkersAsync();
        Assert.IsTrue(markers.TryGetValue(marker, out var frame), $"No frame reported marker '{marker}'.");
        return frame!;
    }

    [TestMethod]
    public async Task CrossOriginFrame_IsDiscoveredWithItsParent()
    {
        var middle = await FrameAsync("middle");

        Assert.AreEqual(_page!.MainFrame, middle.ParentFrame,
            "The cross-origin frame was not attached to the page's main frame.");
        Assert.IsTrue(middle.Url.StartsWith(_server.SecondaryOrigin, StringComparison.Ordinal));

        // Without this the rest of the class would still pass over a same-process frame, and the
        // phase would look covered when the isolation flags had quietly stopped working.
        Assert.IsTrue(((Motus.Page)_page).HasOwnSession(middle),
            "The frame is not rendering in its own process, so this fixture proves nothing.");
    }

    [TestMethod]
    public async Task NestedCrossOriginFrame_IsReachableByTraversal()
    {
        var middle = await FrameAsync("middle");
        var deep = middle.ChildFrames.SingleOrDefault(f => f.Url.EndsWith("/deep.html", StringComparison.Ordinal));

        Assert.IsNotNull(deep, "The frame nested inside the cross-origin frame was never discovered.");
        Assert.AreEqual("deep", await deep.EvaluateAsync<string>("window.marker"));
        Assert.AreEqual("deep", await deep.Locator("#target").TextContentAsync());

        // Two process boundaries lie between this element and the page, so a click that lands is
        // what proves the frame offsets accumulate rather than only handling the first hop.
        await deep.Locator("#target").ClickAsync();
        Assert.AreEqual(1, await deep.EvaluateAsync<int>("window.clicks"));
    }

    [TestMethod]
    public async Task LocatorInCrossOriginFrame_ResolvesInsideThatFrame()
    {
        var middle = await FrameAsync("middle");

        Assert.AreEqual("middle", await middle.Locator("#target").TextContentAsync());
        Assert.AreEqual("main", await _page!.Locator("#target").TextContentAsync());
    }

    [TestMethod]
    public async Task GetByTestId_ScopesToTheCrossOriginFrame()
    {
        var middle = await FrameAsync("middle");

        Assert.AreEqual("middle", await middle.GetByTestId("go").TextContentAsync());
    }

    [TestMethod]
    public async Task RoleSelector_ScopesToTheCrossOriginFrame()
    {
        var middle = await FrameAsync("middle");

        Assert.AreEqual("middle", await middle.Locator("""role=button[name="Go"]""").TextContentAsync());
    }

    [TestMethod]
    public async Task Click_ActsInTheCrossOriginFrame()
    {
        var middle = await FrameAsync("middle");

        await middle.Locator("#target").ClickAsync();

        Assert.AreEqual(1, await middle.EvaluateAsync<int>("window.clicks"),
            "The click did not land on the element, which usually means it was aimed in the frame's "
            + "own coordinate space rather than the page's.");
    }

    /// <summary>
    /// A renderer measures against its own root, so an element inside a frame with its own process
    /// reports coordinates the page knows nothing about. The click test above catches that as a
    /// miss; this one says by how much and in which direction.
    /// </summary>
    [TestMethod]
    public async Task BoundingBox_IsReportedInPageCoordinates()
    {
        var middle = await FrameAsync("middle");

        // Both readings are taken from the page rather than assumed, so the test still means
        // something if the fixture's layout is ever edited.
        var inside = await middle.EvaluateAsync<double[]>(
            """
            (() => {
                const r = document.getElementById('target').getBoundingClientRect();
                return [r.left, r.top];
            })()
            """);

        var frameOrigin = await _page!.EvaluateAsync<double[]>(
            """
            (() => {
                const r = document.getElementById('remote').getBoundingClientRect();
                return [r.left, r.top];
            })()
            """);

        var box = await middle.Locator("#target").BoundingBoxAsync();
        Assert.IsNotNull(box, "The element reported no bounding box.");

        Assert.AreEqual(inside[0] + frameOrigin[0], box!.X, 2.0,
            "The box's X is not the element's offset inside the frame plus the frame's own offset.");
        Assert.AreEqual(inside[1] + frameOrigin[1], box.Y, 2.0,
            $"The box's Y is {box.Y}, but the element sits {inside[1]} inside a frame that starts "
            + $"{frameOrigin[1]} down the page.");
    }

    /// <remarks>
    /// A sandboxed frame has an opaque origin, which under forced site isolation is a site of its
    /// own, so this frame is out of process too even though its content came from the parent
    /// document. It is covered because it reaches that state by a different route than a
    /// cross-origin <c>src</c> does, not because it stays in the page's process.
    /// </remarks>
    [TestMethod]
    public async Task SandboxedSrcdocFrame_ResolvesThroughTheSameRouting()
    {
        var sandboxed = await FrameAsync("sandboxed");

        Assert.AreEqual("about:srcdoc", sandboxed.Url);
        Assert.AreEqual("sandboxed", await sandboxed.Locator("#target").TextContentAsync());
    }

    [TestMethod]
    public async Task IsolatedWorld_SeesTheDomButNotThePageGlobals()
    {
        var middle = await FrameAsync("middle");
        var isolated = new EvaluateOptions { World = ExecutionWorld.Isolated };

        Assert.AreEqual("middle", await middle.EvaluateAsync<string>("window.marker"),
            "The main world should see what the frame's own script defined.");

        Assert.AreEqual("undefined",
            await middle.EvaluateAsync<string>("typeof window.marker", null, isolated),
            "A global defined by page script must not be visible in the isolated world.");

        Assert.AreEqual("middle",
            await middle.EvaluateAsync<string>(
                "document.getElementById('target').textContent", null, isolated),
            "The DOM must be fully present in the isolated world.");
    }

    /// <summary>
    /// Using a frame's isolated world must not change where its main world evaluates.
    /// </summary>
    /// <remarks>
    /// The order is the whole test. A frame has one main world and any number of others beside it,
    /// and every one of them announces the same frame id as it is created, so recording the latest
    /// as the frame's context sends later main-world evaluation into a world where the page's own
    /// globals do not exist. Asking for the main world first hides that entirely, because the
    /// answer is cached before anything else can claim the frame, which is why the main world is
    /// asked for last here.
    /// </remarks>
    [TestMethod]
    public async Task MainWorld_StillAnswersOnceTheIsolatedWorldHasBeenUsed()
    {
        var isolated = new EvaluateOptions { World = ExecutionWorld.Isolated };
        var middle = await FrameAsync("middle");

        Assert.AreEqual("undefined",
            await middle.EvaluateAsync<string>("typeof window.marker", null, isolated));
        Assert.AreEqual("middle", await middle.EvaluateAsync<string>("window.marker"),
            "The frame's main world stopped answering once its isolated world existed.");

        // The main frame is the case that takes the whole page with it: page.EvaluateAsync
        // resolves through the context recorded for the main frame, so a page that never touches
        // a frame at all still loses its globals here.
        Assert.AreEqual("undefined",
            await _page!.MainFrame.EvaluateAsync<string>("typeof window.pageGlobal", null, isolated));
        Assert.AreEqual("outer", await _page.EvaluateAsync<string>("window.pageGlobal"),
            "page.EvaluateAsync stopped seeing the page's globals once an isolated world existed.");
    }

    /// <summary>
    /// An element inside a frame with a process of its own can be screenshotted.
    /// </summary>
    /// <remarks>
    /// Capture belongs to the page, not to the frame: the renderer hosting a frame refuses the
    /// command outright, so routing it to the frame's session fails loudly rather than returning
    /// the wrong picture. Only a frame-rooted locator can reach this element at all. The captured
    /// size is checked as well, because it is what shows the clip was honored rather than the whole
    /// viewport coming back; the clip's coordinate space is already pinned by the click and
    /// bounding-box tests above, which share the same measurement.
    /// </remarks>
    [TestMethod]
    public async Task ElementScreenshotInsideACrossOriginFrame_CapturesJustThatElement()
    {
        var middle = await FrameAsync("middle");
        var target = middle.Locator("#target");

        var box = await target.BoundingBoxAsync();
        Assert.IsNotNull(box);

        var png = await target.ScreenshotAsync();
        var (width, height) = ReadPngSize(png);

        // Chromium rounds a fractional clip out to whole pixels, so this allows a pixel either way
        // rather than pinning the rounding rule itself.
        Assert.IsTrue(Math.Abs(width - box.Width) <= 1,
            $"Captured width {width} does not match the element's {box.Width:F0}.");
        Assert.IsTrue(Math.Abs(height - box.Height) <= 1,
            $"Captured height {height} does not match the element's {box.Height:F0}.");
    }

    /// <summary>
    /// Reads the pixel dimensions out of a PNG's IHDR chunk, which starts at a fixed offset.
    /// </summary>
    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        Assert.IsTrue(png.Length > 24, "The capture is too short to be a PNG.");

        var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        var height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (width, height);
    }

    [TestMethod]
    public async Task DetachingAFrame_LeavesItsHandleReportingDetached()
    {
        var middle = await FrameAsync("middle");
        Assert.IsFalse(middle.IsDetached);

        await _page!.EvaluateAsync<object?>(
            "document.getElementById('remote').remove()");

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!middle.IsDetached && DateTime.UtcNow < deadline)
            await Task.Delay(100);

        Assert.IsTrue(middle.IsDetached, "The removed frame never reported itself detached.");
        Assert.IsFalse(_page.Frames.Contains(middle), "The removed frame is still listed on the page.");
    }

    [TestMethod]
    public async Task ClosingThePage_LeavesItReportingClosed()
    {
        Assert.IsFalse(_page!.IsClosed);

        await _page.CloseAsync();

        Assert.IsTrue(_page.IsClosed);
    }

    [TestMethod]
    public async Task FrameAttached_IsRaisedForFramesFoundAfterTheFirstNavigation()
    {
        var seen = new List<IFrame>();
        _page!.FrameAttached += (_, frame) => { lock (seen) seen.Add(frame); };

        await _page.EvaluateAsync<object?>(
            $$"""
            (() => {
                const f = document.createElement('iframe');
                f.id = 'late';
                f.src = '{{_server.SecondaryOrigin}}/middle.html';
                document.body.appendChild(f);
            })()
            """);

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            lock (seen)
            {
                if (seen.Any(f => f.Url.EndsWith("/middle.html", StringComparison.Ordinal)))
                    return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("No FrameAttached event named the frame that was added after load.");
    }
}
