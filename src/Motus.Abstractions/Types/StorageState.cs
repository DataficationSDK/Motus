namespace Motus.Abstractions;

/// <summary>
/// Represents the complete storage state of a browser context including cookies and local storage.
/// </summary>
/// <param name="Cookies">The cookies in the storage state.</param>
/// <param name="Origins">The local storage entries grouped by origin.</param>
public sealed record StorageState(
    IReadOnlyList<Cookie> Cookies,
    IReadOnlyList<OriginStorage> Origins);
