using Motus.Recorder.CodeEmit;
using Motus.Recorder.Records;

namespace Motus.Recorder.Tests.CodeEmit;

[TestClass]
public class CodeEmitterLocatorTrackingTests
{
    private static readonly DateTimeOffset Ts = new(2024, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private const string Url = "https://example.com/login";
    private readonly CodeEmitter _emitter = new();

    [TestMethod]
    public void EmitWithMetadata_NoActions_EmptyLocators()
    {
        var result = _emitter.EmitWithMetadata(Array.Empty<ResolvedAction>());

        Assert.IsNotNull(result.Source);
        Assert.AreEqual(0, result.Locators.Count);
    }

    [TestMethod]
    public void EmitWithMetadata_IncludesOneEntryPerResolvedAction()
    {
        var actions = new[]
        {
            new ResolvedAction(
                new ClickAction(Ts, Url, null, 100, 200, "left", 1, 0),
                Selector: "#btn",
                LocatorMethod: "Locator",
                BackendNodeId: 501),
            new ResolvedAction(
                new FillAction(Ts, Url, null, 10, 20, "hi"),
                Selector: "#input",
                LocatorMethod: "Locator",
                BackendNodeId: 502),
        };

        var result = _emitter.EmitWithMetadata(actions);

        Assert.AreEqual(2, result.Locators.Count);
        Assert.AreEqual("#btn", result.Locators[0].Selector);
        Assert.AreEqual("Locator", result.Locators[0].LocatorMethod);
        Assert.AreEqual(501, result.Locators[0].BackendNodeId);
        Assert.AreEqual(Url, result.Locators[0].PageUrl);

        Assert.AreEqual("#input", result.Locators[1].Selector);
        Assert.AreEqual(502, result.Locators[1].BackendNodeId);
    }

    [TestMethod]
    public void EmitWithMetadata_LocatorLineNumbers_PointAtExpectedSource()
    {
        var actions = new[]
        {
            new ResolvedAction(
                new ClickAction(Ts, Url, null, 100, 200, "left", 1, 0),
                Selector: "#btn",
                LocatorMethod: "Locator",
                BackendNodeId: 501),
        };

        var result = _emitter.EmitWithMetadata(actions);

        var sourceLines = result.Source.Split('\n');
        var locator = result.Locators[0];
        // SourceLine is 1-based; array is 0-based.
        var line = sourceLines[locator.SourceLine - 1];
        StringAssert.Contains(line, "#btn");
        StringAssert.Contains(line, "ClickAsync");
    }

    [TestMethod]
    public void EmitWithMetadata_SkipsActionsWithoutSelector()
    {
        var actions = new[]
        {
            new ResolvedAction(
                new ClickAction(Ts, Url, null, 100, 200, "left", 1, 0),
                Selector: null,
                LocatorMethod: null,
                BackendNodeId: null),
            new ResolvedAction(
                new NavigationAction(Ts, Url, null, null, null, "https://example.com"),
                Selector: null,
                LocatorMethod: null,
                BackendNodeId: null),
        };

        var result = _emitter.EmitWithMetadata(actions);

        Assert.AreEqual(0, result.Locators.Count);
    }
}
