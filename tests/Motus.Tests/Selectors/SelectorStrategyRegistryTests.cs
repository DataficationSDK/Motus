using Motus.Abstractions;

namespace Motus.Tests.Selectors;

[TestClass]
public class SelectorStrategyRegistryTests
{
    [TestMethod]
    public void Register_And_TryGetStrategy_ReturnsRegistered()
    {
        var registry = new SelectorStrategyRegistry();
        var strategy = new FakeSelectorStrategy("css", 10);

        registry.Register(strategy);

        Assert.IsTrue(registry.TryGetStrategy("css", out var found));
        Assert.AreSame(strategy, found);
    }

    [TestMethod]
    public void Register_Overwrite_ReplacesExisting()
    {
        var registry = new SelectorStrategyRegistry();
        var first = new FakeSelectorStrategy("css", 10);
        var second = new FakeSelectorStrategy("css", 20);

        registry.Register(first);
        registry.Register(second);

        Assert.IsTrue(registry.TryGetStrategy("css", out var found));
        Assert.AreSame(second, found);
    }

    [TestMethod]
    public void TryGetStrategy_CaseInsensitive()
    {
        var registry = new SelectorStrategyRegistry();
        registry.Register(new FakeSelectorStrategy("CSS", 10));

        Assert.IsTrue(registry.TryGetStrategy("css", out _));
        Assert.IsTrue(registry.TryGetStrategy("Css", out _));
        Assert.IsTrue(registry.TryGetStrategy("CSS", out _));
    }

    [TestMethod]
    public void TryGetStrategy_UnknownPrefix_ReturnsFalse()
    {
        var registry = new SelectorStrategyRegistry();
        Assert.IsFalse(registry.TryGetStrategy("unknown", out var found));
        Assert.IsNull(found);
    }

    [TestMethod]
    public void GetDefault_ReturnsCssStrategy()
    {
        var registry = new SelectorStrategyRegistry();
        var css = new FakeSelectorStrategy("css", 10);
        registry.Register(css);

        Assert.AreSame(css, registry.GetDefault());
    }

    [TestMethod]
    public void GetAllByPriority_ReturnsDescendingOrder()
    {
        var registry = new SelectorStrategyRegistry();
        var low = new FakeSelectorStrategy("low", 10);
        var mid = new FakeSelectorStrategy("mid", 20);
        var high = new FakeSelectorStrategy("high", 30);

        registry.Register(low);
        registry.Register(high);
        registry.Register(mid);

        var all = registry.GetAllByPriority();
        Assert.AreEqual(3, all.Count);
        Assert.AreSame(high, all[0]);
        Assert.AreSame(mid, all[1]);
        Assert.AreSame(low, all[2]);
    }

    private sealed class FakeSelectorStrategy : ISelectorStrategy
    {
        public FakeSelectorStrategy(string name, int priority)
        {
            StrategyName = name;
            Priority = priority;
        }

        public string StrategyName { get; }
        public int Priority { get; }

        public Task<IReadOnlyList<IElementHandle>> ResolveAsync(
            string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<IElementHandle>>([]);

        public Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
