namespace Motus.Abstractions;

/// <summary>
/// Specifies the SameSite attribute for cookies.
/// </summary>
public enum SameSiteAttribute
{
    /// <summary>Cookie is sent only with same-site requests.</summary>
    Strict,

    /// <summary>Cookie is sent with same-site requests and top-level cross-site navigations.</summary>
    Lax,

    /// <summary>Cookie is sent with all requests.</summary>
    None
}
