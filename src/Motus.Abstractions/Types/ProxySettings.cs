namespace Motus.Abstractions;

/// <summary>
/// Proxy server configuration.
/// </summary>
/// <param name="Server">The proxy server URL.</param>
/// <param name="Bypass">Comma-separated list of domains to bypass the proxy.</param>
/// <param name="Username">The proxy authentication username.</param>
/// <param name="Password">The proxy authentication password.</param>
public sealed record ProxySettings(
    string Server,
    string? Bypass = null,
    string? Username = null,
    string? Password = null);
