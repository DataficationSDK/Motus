namespace Motus.Abstractions;

/// <summary>
/// Represents an isolated browser context with its own cookies, cache, and storage.
/// </summary>
public interface IBrowserContext : IAsyncDisposable
{
    /// <summary>
    /// Gets the browser that owns this context.
    /// </summary>
    IBrowser Browser { get; }

    /// <summary>
    /// Gets all pages in the context.
    /// </summary>
    IReadOnlyList<IPage> Pages { get; }

    /// <summary>
    /// Gets the tracing controller for this context.
    /// </summary>
    ITracing Tracing { get; }

    // --- Events ---

    /// <summary>
    /// Raised when a new page is created in the context.
    /// </summary>
    event EventHandler<IPage>? Page;

    /// <summary>
    /// Raised when the context is closed.
    /// </summary>
    event EventHandler? Close;

    // --- Pages ---

    /// <summary>
    /// Creates a new page in this context.
    /// </summary>
    /// <returns>The new page.</returns>
    Task<IPage> NewPageAsync();

    // --- Cookies ---

    /// <summary>
    /// Returns all cookies in the context, optionally filtered by URL.
    /// </summary>
    /// <param name="urls">URLs to filter cookies by.</param>
    /// <returns>The cookies.</returns>
    Task<IReadOnlyList<Cookie>> CookiesAsync(IEnumerable<string>? urls = null);

    /// <summary>
    /// Adds cookies to the context.
    /// </summary>
    /// <param name="cookies">The cookies to add.</param>
    Task AddCookiesAsync(IEnumerable<Cookie> cookies);

    /// <summary>
    /// Clears all cookies in the context.
    /// </summary>
    Task ClearCookiesAsync();

    // --- Permissions ---

    /// <summary>
    /// Grants the specified permissions to the context.
    /// </summary>
    /// <param name="permissions">The permissions to grant (e.g. "geolocation", "notifications").</param>
    /// <param name="origin">Optional origin to grant permissions for.</param>
    Task GrantPermissionsAsync(IEnumerable<string> permissions, string? origin = null);

    /// <summary>
    /// Clears all permission overrides.
    /// </summary>
    Task ClearPermissionsAsync();

    // --- Geolocation ---

    /// <summary>
    /// Sets the geolocation for the context.
    /// </summary>
    /// <param name="geolocation">The geolocation to set, or null to clear.</param>
    Task SetGeolocationAsync(Geolocation? geolocation);

    // --- Routing ---

    /// <summary>
    /// Registers a route handler for requests matching the URL pattern.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to match.</param>
    /// <param name="handler">The route handler.</param>
    Task RouteAsync(string urlPattern, Func<IRoute, Task> handler);

    /// <summary>
    /// Removes a previously registered route handler.
    /// </summary>
    /// <param name="urlPattern">The URL pattern to unregister.</param>
    /// <param name="handler">The handler to remove. If null, removes all handlers for the pattern.</param>
    Task UnrouteAsync(string urlPattern, Func<IRoute, Task>? handler = null);

    // --- Network ---

    /// <summary>
    /// Sets whether the context is offline.
    /// </summary>
    /// <param name="offline">Whether to emulate offline mode.</param>
    Task SetOfflineAsync(bool offline);

    /// <summary>
    /// Sets extra HTTP headers for all requests in this context.
    /// </summary>
    /// <param name="headers">The headers to set.</param>
    Task SetExtraHTTPHeadersAsync(IDictionary<string, string> headers);

    // --- Storage ---

    /// <summary>
    /// Returns the storage state for this context.
    /// </summary>
    /// <param name="path">Optional path to save the storage state to.</param>
    /// <returns>The storage state.</returns>
    Task<StorageState> StorageStateAsync(string? path = null);

    // --- Misc ---

    /// <summary>
    /// Exposes a .NET function to be called from JavaScript in all pages.
    /// </summary>
    /// <param name="name">The function name to expose.</param>
    /// <param name="callback">The callback to invoke.</param>
    Task ExposeBindingAsync(string name, Func<object?[], Task<object?>> callback);

    /// <summary>
    /// Adds an initialization script to run in every page created in this context.
    /// </summary>
    /// <param name="script">The script content.</param>
    Task AddInitScriptAsync(string script);

    /// <summary>
    /// Gets the plugin context for registering extensions at runtime.
    /// </summary>
    IPluginContext GetPluginContext();

    /// <summary>
    /// Closes the context and all its pages.
    /// </summary>
    Task CloseAsync();
}
