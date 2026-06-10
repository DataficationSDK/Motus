using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;

namespace Motus.Mcp.Tests.Tools;

[TestClass]
public class CoordinateToolsUnitTests
{
    private static AccessibilityNode Node(string role, string? name, long? backendId)
        => new(
            NodeId: backendId?.ToString() ?? "x",
            Role: role,
            Name: name,
            Value: null,
            Description: null,
            Properties: new Dictionary<string, string?>(),
            Children: [],
            BackendDOMNodeId: backendId);

    private static AccessibilitySnapshot Snapshot(params AccessibilityNode[] roots)
        => new(roots, IgnoredCount: 0, DiagnosticMessage: null);

    private static string TextOf(CallToolResult result)
        => ((TextContentBlock)result.Content[0]).Text;

    private static (FakeToolPage page, FakeActivePageService service) Setup()
    {
        var page = new FakeToolPage(Snapshot(Node("button", "Go", 10)));
        return (page, new FakeActivePageService(page));
    }

    // --- click_xy ---

    [TestMethod]
    public async Task ClickXy_DispatchesClickAtCoordinate()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ClickXyAsync(
            150, 250, @double: null, button: null, modifiers: null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, page.FakeMouse.Clicks.Count);
        Assert.AreEqual((150d, 250d), (page.FakeMouse.Clicks[0].X, page.FakeMouse.Clicks[0].Y));
    }

    [TestMethod]
    public async Task ClickXy_DoubleDispatchesDblClick()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ClickXyAsync(
            10, 20, @double: true, button: null, modifiers: null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(0, page.FakeMouse.Clicks.Count);
        Assert.AreEqual(1, page.FakeMouse.DblClicks.Count);
    }

    [TestMethod]
    public async Task ClickXy_MapsButtonAndModifiers()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ClickXyAsync(
            5, 5, @double: null, button: "right", modifiers: ["Control", "shift"], service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        var options = page.FakeMouse.Clicks[0].Options;
        Assert.IsNotNull(options);
        Assert.AreEqual(MouseButton.Right, options.Button);
        Assert.AreEqual(KeyModifier.Control | KeyModifier.Shift, options.Modifiers);
    }

    [TestMethod]
    public async Task ClickXy_UnknownButtonIsAnError()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ClickXyAsync(
            5, 5, @double: null, button: "back", modifiers: null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError ?? false);
        Assert.AreEqual(0, page.FakeMouse.Clicks.Count);
    }

    [TestMethod]
    public async Task ClickXy_UnknownModifierIsAnError()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ClickXyAsync(
            5, 5, @double: null, button: null, modifiers: ["Hyper"], service, CancellationToken.None);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "Hyper");
        Assert.AreEqual(0, page.FakeMouse.Clicks.Count);
    }

    // --- hover_xy / move_xy ---

    [TestMethod]
    public async Task HoverXy_MovesPointer()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.HoverXyAsync(30, 40, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, page.FakeMouse.Moves.Count);
        Assert.AreEqual((30d, 40d), (page.FakeMouse.Moves[0].X, page.FakeMouse.Moves[0].Y));
    }

    [TestMethod]
    public async Task MoveXy_MovesPointer()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.MoveXyAsync(60, 70, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(1, page.FakeMouse.Moves.Count);
        Assert.AreEqual((60d, 70d), (page.FakeMouse.Moves[0].X, page.FakeMouse.Moves[0].Y));
    }

    // --- scroll_xy ---

    [TestMethod]
    public async Task ScrollXy_MovesThenWheels()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ScrollXyAsync(100, 100, 0, 240, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.AreEqual(new[] { "move", "wheel" }, page.FakeMouse.Sequence);
        Assert.AreEqual((100d, 100d), (page.FakeMouse.Moves[0].X, page.FakeMouse.Moves[0].Y));
        Assert.AreEqual((0d, 240d), page.FakeMouse.Wheels[0]);
    }

    // --- resize ---

    [TestMethod]
    public async Task Resize_SetsViewportSize()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ResizeAsync(1920, 1080, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        Assert.AreEqual(new ViewportSize(1920, 1080), page.ResizedTo);
    }

    [TestMethod]
    public async Task Resize_RejectsNonPositiveSize()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.ResizeAsync(0, 600, service, CancellationToken.None);

        Assert.IsTrue(result.IsError ?? false);
        Assert.IsNull(page.ResizedTo);
    }

    // --- drag ---

    [TestMethod]
    public async Task Drag_ByCoordinates_PressesMovesReleases()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.DragAsync(
            start_ref: null, end_ref: null,
            start_x: 10, start_y: 20, end_x: 200, end_y: 220,
            steps: 5, hold_ms: null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.AreEqual(new[] { "move", "down", "move", "up" }, page.FakeMouse.Sequence);
        Assert.AreEqual((10d, 20d), (page.FakeMouse.Moves[0].X, page.FakeMouse.Moves[0].Y));
        Assert.AreEqual((200d, 220d), (page.FakeMouse.Moves[1].X, page.FakeMouse.Moves[1].Y));
        Assert.AreEqual(5, page.FakeMouse.Moves[1].Options?.Steps);
    }

    [TestMethod]
    public async Task Drag_DefaultsToTenSteps()
    {
        var (page, service) = Setup();

        await CoordinateTools.DragAsync(
            start_ref: null, end_ref: null,
            start_x: 0, start_y: 0, end_x: 50, end_y: 50,
            steps: null, hold_ms: null, service, CancellationToken.None);

        Assert.AreEqual(10, page.FakeMouse.Moves[1].Options?.Steps);
    }

    [TestMethod]
    public async Task Drag_ByRefs_UsesBoundingBoxCenters()
    {
        var page = new FakeToolPage(Snapshot(Node("listitem", "Revenue", 10)));
        var service = new FakeActivePageService(page);
        await CoreTools.SnapshotAsync(null, null, service, CancellationToken.None);
        page.RecordingLocator.Box = new BoundingBox(100, 200, 50, 30);

        var result = await CoordinateTools.DragAsync(
            start_ref: "e1", end_ref: "e1",
            start_x: null, start_y: null, end_x: null, end_y: null,
            steps: null, hold_ms: null, service, CancellationToken.None);

        Assert.IsFalse(result.IsError ?? false);
        CollectionAssert.AreEqual(new[] { "move", "down", "move", "up" }, page.FakeMouse.Sequence);
        Assert.AreEqual((125d, 215d), (page.FakeMouse.Moves[0].X, page.FakeMouse.Moves[0].Y));
    }

    [TestMethod]
    public async Task Drag_ByRefs_WithoutSnapshotGivesGuidance()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.DragAsync(
            start_ref: "e1", end_ref: "e2",
            start_x: null, start_y: null, end_x: null, end_y: null,
            steps: null, hold_ms: null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError ?? false);
        StringAssert.Contains(TextOf(result), "snapshot");
        Assert.AreEqual(0, page.FakeMouse.Sequence.Count);
    }

    [TestMethod]
    public async Task Drag_MixedAddressingIsAnError()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.DragAsync(
            start_ref: "e1", end_ref: null,
            start_x: null, start_y: null, end_x: 50, end_y: 50,
            steps: null, hold_ms: null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError ?? false);
        Assert.AreEqual(0, page.FakeMouse.Sequence.Count);
    }

    [TestMethod]
    public async Task Drag_PartialCoordinatesIsAnError()
    {
        var (page, service) = Setup();

        var result = await CoordinateTools.DragAsync(
            start_ref: null, end_ref: null,
            start_x: 10, start_y: null, end_x: 50, end_y: 50,
            steps: null, hold_ms: null, service, CancellationToken.None);

        Assert.IsTrue(result.IsError ?? false);
        Assert.AreEqual(0, page.FakeMouse.Sequence.Count);
    }
}
