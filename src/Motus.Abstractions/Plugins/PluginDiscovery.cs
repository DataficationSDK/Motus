namespace Motus.Abstractions;

/// <summary>
/// Bridge for source-generated plugin registration. Do not call directly.
/// </summary>
public static class PluginDiscovery
{
    /// <summary>
    /// Factory delegate set by the generated module initializer.
    /// Returns all discovered plugin instances.
    /// </summary>
    public static Func<IPlugin[]>? Factory { get; set; }
}
