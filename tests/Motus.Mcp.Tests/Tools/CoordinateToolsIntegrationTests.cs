using ModelContextProtocol.Protocol;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Drives the coordinate tools through a real browser against a canvas page that
/// has no addressable accessibility nodes for its painted controls, the surface
/// these tools exist for: resize the viewport, click a painted control, wheel
/// scroll over it, and drag between two painted regions, verifying each through
/// the events the page observed.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class CoordinateToolsIntegrationTests
{
    // A canvas with a painted "button" (blue, at 80,80 size 60x40) and a painted
    // "drop zone" (red, at 400,300 size 80x60). Listeners record what trusted
    // input the canvas actually received. Note: data: URLs cannot contain '#'.
    private const string CanvasPage =
        "data:text/html,<body style=\"margin:0\">"
        + "<canvas id=c width=600 height=400></canvas>"
        + "<div id=clicked></div><div id=wheeled></div><div id=dragged></div><div id=moved></div>"
        + "<script>"
        + "const c=document.getElementById('c');"
        + "const x=c.getContext('2d');"
        + "x.fillStyle='blue';x.fillRect(80,80,60,40);"
        + "x.fillStyle='red';x.fillRect(400,300,80,60);"
        + "c.addEventListener('click',e=>{document.getElementById('clicked').textContent='click:'+e.offsetX+','+e.offsetY;});"
        + "c.addEventListener('wheel',e=>{document.getElementById('wheeled').textContent='wheel:'+e.deltaY;});"
        + "c.addEventListener('mousemove',e=>{document.getElementById('moved').textContent='move:'+e.offsetX+','+e.offsetY;});"
        + "let d=null;"
        + "c.addEventListener('mousedown',e=>{d={x:e.offsetX,y:e.offsetY};});"
        + "c.addEventListener('mouseup',e=>{if(d)document.getElementById('dragged').textContent='drag:'+d.x+','+d.y+'-'+e.offsetX+','+e.offsetY;});"
        + "</script></body>";

    private BrowserSessionManager? _sessions;
    private ActivePageService? _pages;

    [TestInitialize]
    public void Setup()
    {
        var executablePath = ResolveInstalledBrowser();
        if (executablePath is null)
            Assert.Inconclusive("No installed browser found; skipping integration test.");

        _sessions = new BrowserSessionManager(new McpServerLaunchOptions
        {
            Headless = true,
            ExecutablePath = executablePath,
        });
        _pages = new ActivePageService(_sessions);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_pages is not null)
            _pages.Shutdown();
        if (_sessions is not null)
            await _sessions.DisposeAsync();
    }

    [TestMethod]
    public async Task CoordinateSequence_OnACanvasPage()
    {
        var service = _pages!;
        var ct = CancellationToken.None;

        AssertOk(await CoreTools.NavigateAsync(CanvasPage, service, ct), "navigate");
        var page = await service.GetOrCreateActivePageAsync(ct);

        // Resize: the page sees the new viewport.
        AssertOk(await CoordinateTools.ResizeAsync(1000, 700, service, ct), "resize");
        Assert.AreEqual(1000, await page.EvaluateAsync<int>("window.innerWidth"));
        Assert.AreEqual(700, await page.EvaluateAsync<int>("window.innerHeight"));

        // Click the painted button. With margin 0 the canvas sits at the page
        // origin, so viewport coordinates equal canvas offsets.
        AssertOk(await CoordinateTools.ClickXyAsync(110, 100, null, null, null, service, ct), "click_xy");
        var clicked = await page.EvaluateAsync<string>("document.getElementById('clicked').textContent");
        Assert.AreEqual("click:110,100", clicked);

        // Hover: the canvas observes the pointer move.
        AssertOk(await CoordinateTools.HoverXyAsync(90, 90, service, ct), "hover_xy");
        var moved = await page.EvaluateAsync<string>("document.getElementById('moved').textContent");
        Assert.AreEqual("move:90,90", moved);

        // Wheel scroll over the canvas: the wheel event arrives with the delta.
        AssertOk(await CoordinateTools.ScrollXyAsync(200, 200, 0, 120, service, ct), "scroll_xy");
        var wheeled = await page.EvaluateAsync<string>("document.getElementById('wheeled').textContent");
        Assert.AreEqual("wheel:120", wheeled);

        // Drag from the painted button onto the painted drop zone.
        AssertOk(await CoordinateTools.DragAsync(
            null, null, start_x: 110, start_y: 100, end_x: 440, end_y: 330,
            steps: 8, hold_ms: null, service, ct), "drag");
        var dragged = await page.EvaluateAsync<string>("document.getElementById('dragged').textContent");
        Assert.AreEqual("drag:110,100-440,330", dragged);
    }

    private static void AssertOk(CallToolResult result, string label)
        => Assert.IsFalse(result.IsError ?? false, $"{label} should succeed: {TextOf(result)}");

    private static string TextOf(CallToolResult result)
        => result.Content[0] is TextContentBlock t ? t.Text : string.Empty;

    private static string? ResolveInstalledBrowser()
    {
        var cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".motus",
            "browsers");

        foreach (var marker in new[] { ".installed.chromium", ".installed" })
        {
            var markerPath = Path.Combine(cacheDir, marker);
            if (!File.Exists(markerPath))
                continue;

            var executablePath = File.ReadAllText(markerPath).Trim();
            if (File.Exists(executablePath))
                return executablePath;
        }

        return null;
    }
}
