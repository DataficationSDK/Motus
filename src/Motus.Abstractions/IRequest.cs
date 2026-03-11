namespace Motus.Abstractions;

/// <summary>
/// Represents an HTTP request sent by the page.
/// </summary>
public interface IRequest
{
    /// <summary>
    /// Gets the URL of the request.
    /// </summary>
    string Url { get; }

    /// <summary>
    /// Gets the HTTP method of the request (e.g. "GET", "POST").
    /// </summary>
    string Method { get; }

    /// <summary>
    /// Gets the request headers.
    /// </summary>
    IHeaderCollection Headers { get; }

    /// <summary>
    /// Gets the request post data, or null if there is none.
    /// </summary>
    string? PostData { get; }

    /// <summary>
    /// Gets the resource type of the request (e.g. "document", "script", "image").
    /// </summary>
    string ResourceType { get; }

    /// <summary>
    /// Gets whether this request is a navigation request.
    /// </summary>
    bool IsNavigationRequest { get; }

    /// <summary>
    /// Gets the frame that initiated this request.
    /// </summary>
    IFrame? Frame { get; }

    /// <summary>
    /// Returns the response for this request, or null if the request was not fulfilled.
    /// </summary>
    /// <returns>The response, or null.</returns>
    Task<IResponse?> ResponseAsync();
}
