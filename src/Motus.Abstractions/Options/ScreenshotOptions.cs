namespace Motus.Abstractions;

/// <summary>
/// Options for taking a screenshot.
/// </summary>
public sealed record ScreenshotOptions
{
    /// <summary>The image format.</summary>
    public ScreenshotType? Type { get; init; }

    /// <summary>The quality of the image (0-100). Only applicable for JPEG.</summary>
    public int? Quality { get; init; }

    /// <summary>Whether to capture the full scrollable page.</summary>
    public bool? FullPage { get; init; }

    /// <summary>The clipping region for the screenshot.</summary>
    public ClipRect? Clip { get; init; }

    /// <summary>Whether to hide the default white background and allow transparency.</summary>
    public bool? OmitBackground { get; init; }

    /// <summary>Path to save the screenshot to.</summary>
    public string? Path { get; init; }
}
