using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Manages plugin lifecycle: discovery, loading, and unloading.
/// </summary>
internal sealed class PluginHost
{
    private readonly List<IPlugin> _plugins = [];

    internal IReadOnlyList<IPlugin> Plugins => _plugins;

    internal async Task LoadAsync(LaunchOptions options, BrowserContext context)
    {
        var pluginContext = context.GetPluginContext();

        // 1. Load built-in plugins (failures propagate - these are required)
        var builtins = new IPlugin[] { new BuiltinSelectorsPlugin(), new AccessibilityRulesPlugin() };
        foreach (var plugin in builtins)
        {
            await plugin.OnLoadedAsync(pluginContext).ConfigureAwait(false);
            _plugins.Add(plugin);
        }

        // 2. Get auto-discovered plugins via bridge
        var discovered = PluginDiscovery.Factory?.Invoke() ?? [];

        // 3. Get manually registered plugins
        var manual = options.Plugins ?? [];

        // 4. Merge: manual takes precedence, no duplicates by PluginId
        var seen = new HashSet<string>(StringComparer.Ordinal);
        // Exclude built-in IDs from dedup so user plugins can't accidentally suppress them
        foreach (var plugin in builtins)
            seen.Add(plugin.PluginId);

        var merged = new List<IPlugin>();
        foreach (var plugin in manual)
        {
            if (seen.Add(plugin.PluginId))
                merged.Add(plugin);
        }
        foreach (var plugin in discovered)
        {
            if (seen.Add(plugin.PluginId))
                merged.Add(plugin);
        }

        // 5. Load user/discovered plugins (failures swallowed per-plugin)
        foreach (var plugin in merged)
        {
            try
            {
                await plugin.OnLoadedAsync(pluginContext).ConfigureAwait(false);
                _plugins.Add(plugin);
            }
            catch
            {
                // Skip plugins that fail to load
            }
        }
    }

    internal async Task UnloadAsync()
    {
        // Unload in reverse order
        for (int i = _plugins.Count - 1; i >= 0; i--)
        {
            try
            {
                await _plugins[i].OnUnloadedAsync().ConfigureAwait(false);
            }
            catch
            {
                // Swallow to ensure all plugins get a chance to unload
            }
        }

        _plugins.Clear();
    }
}
