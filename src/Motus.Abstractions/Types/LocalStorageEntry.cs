namespace Motus.Abstractions;

/// <summary>
/// Represents a single local storage key-value entry.
/// </summary>
/// <param name="Name">The storage key name.</param>
/// <param name="Value">The storage value.</param>
public sealed record LocalStorageEntry(string Name, string Value);
