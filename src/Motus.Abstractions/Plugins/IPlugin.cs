namespace Motus.Abstractions;

/// <summary>
/// Base interface for all Motus plugins.
/// </summary>
public interface IPlugin
{
    /// <summary>
    /// Gets the unique identifier for the plugin.
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Gets the display name of the plugin.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the version of the plugin.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Gets the author of the plugin.
    /// </summary>
    string? Author { get; }

    /// <summary>
    /// Gets a description of the plugin.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Called when the plugin is loaded into the engine.
    /// </summary>
    /// <param name="context">The plugin context for registering extensions.</param>
    Task OnLoadedAsync(IPluginContext context);

    /// <summary>
    /// Called when the plugin is being unloaded from the engine.
    /// </summary>
    Task OnUnloadedAsync();
}
