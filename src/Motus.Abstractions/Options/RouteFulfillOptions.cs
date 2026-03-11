namespace Motus.Abstractions;

/// <summary>
/// Options for fulfilling a route with a custom response.
/// </summary>
public sealed record RouteFulfillOptions
{
    /// <summary>The HTTP status code to use.</summary>
    public int? Status { get; init; }

    /// <summary>Response headers.</summary>
    public IDictionary<string, string>? Headers { get; init; }

    /// <summary>Response body as a string.</summary>
    public string? Body { get; init; }

    /// <summary>Response body as raw bytes.</summary>
    public byte[]? BodyBytes { get; init; }

    /// <summary>The Content-Type response header.</summary>
    public string? ContentType { get; init; }

    /// <summary>Path to a file to serve as the response body.</summary>
    public string? Path { get; init; }
}
