using Motus.Recorder.PageAnalysis;
using Motus.Recorder.PomEmit;

namespace Motus.Recorder.Tests.PomEmit;

[TestClass]
public class PomEmitterLocatorTrackingTests
{
    private readonly PomEmitter _emitter = new();

    private static DiscoveredElement Element(
        string memberName, string? selector, int? backendNodeId = null, int elementIndex = 0)
        => new(
            new PageElementInfo("input", "text", null, null, null, null, null, null, null, null, null, elementIndex),
            selector,
            memberName,
            backendNodeId);

    [TestMethod]
    public void EmitWithMetadata_NoElements_EmptyLocators()
    {
        var result = _emitter.EmitWithMetadata(Array.Empty<DiscoveredElement>());

        Assert.IsNotNull(result.Source);
        Assert.AreEqual(0, result.Locators.Count);
    }

    [TestMethod]
    public void EmitWithMetadata_OneEntryPerElementWithSelector()
    {
        var options = new PomEmitOptions
        {
            Namespace = "Test.Gen",
            ClassName = "LoginPage",
            PageUrl = "https://example.com/login",
        };

        var elements = new[]
        {
            Element("EmailInput", "#email", backendNodeId: 100, elementIndex: 0),
            Element("PasswordInput", "#pass", backendNodeId: 101, elementIndex: 1),
            Element("NoSelectorInput", selector: null, backendNodeId: 102, elementIndex: 2),
        };

        var result = _emitter.EmitWithMetadata(elements, options);

        Assert.AreEqual(2, result.Locators.Count);
        Assert.AreEqual("#email", result.Locators[0].Selector);
        Assert.AreEqual("Locator", result.Locators[0].LocatorMethod);
        Assert.AreEqual(100, result.Locators[0].BackendNodeId);
        Assert.AreEqual("https://example.com/login", result.Locators[0].PageUrl);

        Assert.AreEqual("#pass", result.Locators[1].Selector);
        Assert.AreEqual(101, result.Locators[1].BackendNodeId);
    }

    [TestMethod]
    public void EmitWithMetadata_LineNumbers_PointAtLocatorProperty()
    {
        var elements = new[] { Element("EmailInput", "#email", backendNodeId: 100) };

        var result = _emitter.EmitWithMetadata(elements);
        var sourceLines = result.Source.Split('\n');
        var locator = result.Locators[0];
        var line = sourceLines[locator.SourceLine - 1];

        StringAssert.Contains(line, "EmailInput");
        StringAssert.Contains(line, "#email");
        StringAssert.Contains(line, "public ILocator");
    }
}
