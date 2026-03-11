namespace Motus.Abstractions;

/// <summary>
/// Options for continuing an intercepted route with modifications.
/// </summary>
/// <param name="Url">Optional URL override.</param>
/// <param name="Method">Optional HTTP method override.</param>
/// <param name="Headers">Optional headers override.</param>
/// <param name="PostData">Optional post data override.</param>
public sealed record RouteContinueOptions(
    string? Url = null,
    string? Method = null,
    IDictionary<string, string>? Headers = null,
    string? PostData = null);
