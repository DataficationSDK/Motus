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
        // 1. Get auto-discovered plugins via bridge
        var discovered = PluginDiscovery.Factory?.Invoke() ?? [];

        // 2. Get manually registered plugins
        var manual = options.Plugins ?? [];

        // 3. Merge: manual takes precedence, no duplicates by PluginId
        var seen = new HashSet<string>(StringComparer.Ordinal);
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

        // 4. Load each plugin
        var pluginContext = context.GetPluginContext();
        foreach (var plugin in merged)
        {
            try
            {
                await plugin.OnLoadedAsync(pluginContext);
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
                await _plugins[i].OnUnloadedAsync();
            }
            catch
            {
                // Swallow to ensure all plugins get a chance to unload
            }
        }

        _plugins.Clear();
    }
}
