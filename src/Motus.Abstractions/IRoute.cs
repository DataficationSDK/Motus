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
    /// <param name="url">Optional URL override.</param>
    /// <param name="method">Optional HTTP method override.</param>
    /// <param name="headers">Optional headers override.</param>
    /// <param name="postData">Optional post data override.</param>
    Task ContinueAsync(string? url = null, string? method = null, IDictionary<string, string>? headers = null, string? postData = null);

    /// <summary>
    /// Aborts the route.
    /// </summary>
    /// <param name="errorCode">Optional error code (e.g. "aborted", "accessdenied", "connectionrefused").</param>
    Task AbortAsync(string? errorCode = null);
}
