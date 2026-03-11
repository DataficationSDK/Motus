namespace Motus.Abstractions;

/// <summary>
/// Represents local storage entries for a specific origin.
/// </summary>
/// <param name="Origin">The origin URL.</param>
/// <param name="LocalStorage">The local storage key-value entries for this origin.</param>
public sealed record OriginStorage(string Origin, IReadOnlyList<KeyValuePair<string, string>> LocalStorage);
