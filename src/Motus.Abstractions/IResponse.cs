namespace Motus.Abstractions;

/// <summary>
/// Represents an HTTP response received by the page.
/// </summary>
public interface IResponse
{
    /// <summary>
    /// Gets the URL of the response.
    /// </summary>
    string Url { get; }

    /// <summary>
    /// Gets the HTTP status code of the response.
    /// </summary>
    int Status { get; }

    /// <summary>
    /// Gets the HTTP status text of the response.
    /// </summary>
    string StatusText { get; }

    /// <summary>
    /// Gets the response headers.
    /// </summary>
    IHeaderCollection Headers { get; }

    /// <summary>
    /// Gets whether the response status code is in the 200-299 range.
    /// </summary>
    bool Ok { get; }

    /// <summary>
    /// Gets the request that generated this response.
    /// </summary>
    IRequest Request { get; }

    /// <summary>
    /// Returns the response body as a byte array.
    /// </summary>
    /// <returns>The response body bytes.</returns>
    Task<byte[]> BodyAsync();

    /// <summary>
    /// Returns the response body as text.
    /// </summary>
    /// <returns>The response body text.</returns>
    Task<string> TextAsync();

    /// <summary>
    /// Deserializes the response body as JSON.
    /// </summary>
    /// <typeparam name="T">The expected deserialized type.</typeparam>
    /// <returns>The deserialized object.</returns>
    Task<T> JsonAsync<T>();
}
