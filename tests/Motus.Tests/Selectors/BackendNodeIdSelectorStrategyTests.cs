namespace Motus.Tests.Selectors;

[TestClass]
public class BackendNodeIdSelectorStrategyTests
{
    [TestMethod]
    public void Registers_Under_NodePrefix()
    {
        var strategy = new BackendNodeIdSelectorStrategy();
        Assert.AreEqual("_node", strategy.StrategyName);
        Assert.AreEqual(BackendNodeIdSelectorStrategy.Prefix, strategy.StrategyName);
    }

    [TestMethod]
    public void Priority_OutranksContentStrategies()
    {
        var strategy = new BackendNodeIdSelectorStrategy();
        Assert.IsTrue(strategy.Priority >= 100);
    }

    [TestMethod]
    public async Task GenerateSelector_ReturnsNull()
    {
        var strategy = new BackendNodeIdSelectorStrategy();
        var generated = await strategy.GenerateSelector(element: null!);
        Assert.IsNull(generated);
    }

    [TestMethod]
    public void BuiltinPlugin_RegistersBackendNodeStrategy()
    {
        var registry = new SelectorStrategyRegistry();
        registry.Register(new BackendNodeIdSelectorStrategy());

        Assert.IsTrue(registry.TryGetStrategy("_node", out var found));
        Assert.IsInstanceOfType(found, typeof(BackendNodeIdSelectorStrategy));
    }
}
