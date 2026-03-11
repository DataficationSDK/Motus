namespace Motus.Abstractions;

/// <summary>
/// Options for creating a new browser context.
/// </summary>
public sealed record ContextOptions
{
    /// <summary>The viewport size. Null disables the default viewport.</summary>
    public ViewportSize? Viewport { get; init; }

    /// <summary>The locale to use, e.g. "en-US".</summary>
    public string? Locale { get; init; }

    /// <summary>The timezone identifier, e.g. "America/New_York".</summary>
    public string? TimezoneId { get; init; }

    /// <summary>The geolocation to emulate.</summary>
    public Geolocation? Geolocation { get; init; }

    /// <summary>Permissions to grant to all pages in this context.</summary>
    public IEnumerable<string>? Permissions { get; init; }

    /// <summary>The preferred color scheme.</summary>
    public ColorScheme? ColorScheme { get; init; }

    /// <summary>The user agent string to use.</summary>
    public string? UserAgent { get; init; }

    /// <summary>Whether to ignore HTTPS errors.</summary>
    public bool? IgnoreHttpsErrors { get; init; }

    /// <summary>HTTP credentials for HTTP authentication.</summary>
    public HttpCredentials? HttpCredentials { get; init; }

    /// <summary>Proxy settings for this context.</summary>
    public ProxySettings? Proxy { get; init; }

    /// <summary>Whether to record video for all pages. Requires <see cref="RecordVideoOptions"/>.</summary>
    public RecordVideoOptions? RecordVideo { get; init; }

    /// <summary>Storage state to initialize the context with.</summary>
    public StorageState? StorageState { get; init; }

    /// <summary>Extra HTTP headers to send with every request.</summary>
    public IDictionary<string, string>? ExtraHttpHeaders { get; init; }

    /// <summary>Whether to bypass content security policy.</summary>
    public bool? BypassCSP { get; init; }

    /// <summary>Whether the context is offline.</summary>
    public bool? Offline { get; init; }
}
