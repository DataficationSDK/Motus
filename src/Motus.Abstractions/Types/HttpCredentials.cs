namespace Motus.Abstractions;

/// <summary>
/// Credentials for HTTP authentication.
/// </summary>
/// <param name="Username">The username.</param>
/// <param name="Password">The password.</param>
public sealed record HttpCredentials(string Username, string Password);
