using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Internal plugin that registers the five built-in selector strategies
/// through the same IPluginContext path that third-party plugins use.
/// </summary>
internal sealed class BuiltinSelectorsPlugin : IPlugin
{
    public string PluginId => "motus.builtin.selectors";
    public string Name => "Built-in Selector Strategies";
    public string Version => "1.0.0";
    public string? Author => null;
    public string? Description => "Registers the five built-in selector strategies (CSS, XPath, Text, Role, TestId).";

    public Task OnLoadedAsync(IPluginContext context)
    {
        context.RegisterSelectorStrategy(new CssSelectorStrategy());
        context.RegisterSelectorStrategy(new XPathSelectorStrategy());
        context.RegisterSelectorStrategy(new TextSelectorStrategy());
        context.RegisterSelectorStrategy(new RoleSelectorStrategy());
        context.RegisterSelectorStrategy(new TestIdSelectorStrategy());
        return Task.CompletedTask;
    }

    public Task OnUnloadedAsync() => Task.CompletedTask;
}
