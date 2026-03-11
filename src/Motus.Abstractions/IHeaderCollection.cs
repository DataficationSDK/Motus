namespace Motus.Abstractions;

/// <summary>
/// Represents a read-only collection of HTTP headers.
/// </summary>
public interface IHeaderCollection : IEnumerable<KeyValuePair<string, string>>
{
    /// <summary>
    /// Gets the value of the header with the specified name.
    /// </summary>
    /// <param name="name">The header name (case-insensitive).</param>
    /// <returns>The header value, or null if the header is not present.</returns>
    string? this[string name] { get; }

    /// <summary>
    /// Gets all values for the header with the specified name.
    /// </summary>
    /// <param name="name">The header name (case-insensitive).</param>
    /// <returns>A list of all values for the header.</returns>
    IReadOnlyList<string> GetAll(string name);

    /// <summary>
    /// Returns whether the collection contains a header with the specified name.
    /// </summary>
    /// <param name="name">The header name (case-insensitive).</param>
    /// <returns>True if the header is present.</returns>
    bool Contains(string name);
}
