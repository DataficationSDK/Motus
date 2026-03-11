namespace Motus.Abstractions;

/// <summary>
/// Represents a browser cookie.
/// </summary>
/// <param name="Name">The cookie name.</param>
/// <param name="Value">The cookie value.</param>
/// <param name="Domain">The cookie domain.</param>
/// <param name="Path">The cookie path.</param>
/// <param name="Expires">The cookie expiration time as a Unix timestamp, or -1 for session cookies.</param>
/// <param name="HttpOnly">Whether the cookie is HTTP-only.</param>
/// <param name="Secure">Whether the cookie requires a secure connection.</param>
/// <param name="SameSite">The cookie SameSite attribute.</param>
public sealed record Cookie(
    string Name,
    string Value,
    string Domain,
    string Path,
    double Expires,
    bool HttpOnly,
    bool Secure,
    SameSiteAttribute SameSite);
