namespace Motus.Abstractions;

/// <summary>
/// Represents an intercepted network route that can be fulfilled, continued, or aborted.
/// </summary>
public interface IRoute
{
    /// <summary>
    /// Gets the request associated with this route.
    /// </summary>
    IRequest Request { get; }

    /// <summary>
    /// Fulfills the route with a custom response.
    /// </summary>
    /// <param name="options">Options for the response.</param>
    Task FulfillAsync(RouteFulfillOptions? options = null);

    /// <summary>
    /// Continues the route with optional modifications to the request.
    /// </summary>
    /// <param name="options">Options for continuing the route.</param>
    Task ContinueAsync(RouteContinueOptions? options = null);

    /// <summary>
    /// Aborts the route.
    /// </summary>
    /// <param name="errorCode">Optional error code (e.g. "aborted", "accessdenied", "connectionrefused").</param>
    Task AbortAsync(string? errorCode = null);
}
