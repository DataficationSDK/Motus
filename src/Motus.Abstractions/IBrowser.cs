namespace Motus.Abstractions;

/// <summary>
/// Represents a browser instance. A browser can have multiple contexts.
/// </summary>
public interface IBrowser : IAsyncDisposable
{
    /// <summary>
    /// Gets whether the browser is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets all browser contexts.
    /// </summary>
    IReadOnlyList<IBrowserContext> Contexts { get; }

    /// <summary>
    /// Gets the browser version string.
    /// </summary>
    string Version { get; }

    /// <summary>
    /// Raised when the browser is disconnected.
    /// </summary>
    event EventHandler? Disconnected;

    /// <summary>
    /// Creates a new browser context.
    /// </summary>
    /// <param name="options">Context options.</param>
    /// <returns>The new browser context.</returns>
    Task<IBrowserContext> NewContextAsync(ContextOptions? options = null);

    /// <summary>
    /// Creates a new page in a new browser context. Convenience method equivalent to creating a context and then a page.
    /// </summary>
    /// <param name="options">Context options.</param>
    /// <returns>The new page.</returns>
    Task<IPage> NewPageAsync(ContextOptions? options = null);

    /// <summary>
    /// Closes the browser and all its contexts.
    /// </summary>
    Task CloseAsync();
}
