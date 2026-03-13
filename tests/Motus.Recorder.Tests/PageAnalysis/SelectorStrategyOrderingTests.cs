using Motus.Abstractions;
using Motus.Recorder.PageAnalysis;

namespace Motus.Recorder.Tests.PageAnalysis;

[TestClass]
public class SelectorStrategyOrderingTests
{
    private sealed class FakeStrategy(string name, int priority) : ISelectorStrategy
    {
        public string StrategyName => name;
        public int Priority => priority;

        public Task<IReadOnlyList<IElementHandle>> ResolveAsync(
            string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IElementHandle>>([]);

        public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    private static readonly IReadOnlyList<ISelectorStrategy> DefaultStrategies =
    [
        new FakeStrategy("testid", 100),
        new FakeStrategy("role", 90),
        new FakeStrategy("text", 80),
        new FakeStrategy("css", 70),
        new FakeStrategy("xpath", 60),
    ];

    [TestMethod]
    public void Reorder_NullPriority_ReturnsOriginal()
    {
        var result = SelectorStrategyOrdering.Reorder(DefaultStrategies, null);
        Assert.AreSame(DefaultStrategies, result);
    }

    [TestMethod]
    public void Reorder_EmptyPriority_ReturnsOriginal()
    {
        var result = SelectorStrategyOrdering.Reorder(DefaultStrategies, []);
        Assert.AreSame(DefaultStrategies, result);
    }

    [TestMethod]
    public void Reorder_CustomPriority_ReordersSpecifiedFirst()
    {
        var result = SelectorStrategyOrdering.Reorder(DefaultStrategies, ["css", "text"]);

        Assert.AreEqual("css", result[0].StrategyName);
        Assert.AreEqual("text", result[1].StrategyName);
        // Remaining strategies follow in original order
        Assert.AreEqual("testid", result[2].StrategyName);
        Assert.AreEqual("role", result[3].StrategyName);
        Assert.AreEqual("xpath", result[4].StrategyName);
    }

    [TestMethod]
    public void Reorder_CaseInsensitive()
    {
        var result = SelectorStrategyOrdering.Reorder(DefaultStrategies, ["CSS", "ROLE"]);

        Assert.AreEqual("css", result[0].StrategyName);
        Assert.AreEqual("role", result[1].StrategyName);
    }

    [TestMethod]
    public void Reorder_UnknownNames_Ignored()
    {
        var result = SelectorStrategyOrdering.Reorder(DefaultStrategies, ["unknown", "css"]);

        Assert.AreEqual("css", result[0].StrategyName);
        Assert.AreEqual(5, result.Count);
    }

    [TestMethod]
    public void Reorder_AllStrategiesPresent()
    {
        var result = SelectorStrategyOrdering.Reorder(DefaultStrategies, ["xpath", "css", "text", "role", "testid"]);

        Assert.AreEqual("xpath", result[0].StrategyName);
        Assert.AreEqual("css", result[1].StrategyName);
        Assert.AreEqual("text", result[2].StrategyName);
        Assert.AreEqual("role", result[3].StrategyName);
        Assert.AreEqual("testid", result[4].StrategyName);
    }
}
