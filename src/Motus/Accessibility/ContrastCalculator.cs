using System.Globalization;

namespace Motus;

/// <summary>
/// Implements WCAG 2.1 relative luminance and contrast ratio calculations.
/// </summary>
internal static class ContrastCalculator
{
    /// <summary>
    /// WCAG AA minimum contrast ratio for normal text.
    /// </summary>
    internal const double NormalTextThreshold = 4.5;

    /// <summary>
    /// WCAG AA minimum contrast ratio for large text (>= 18pt or bold >= 14pt).
    /// </summary>
    internal const double LargeTextThreshold = 3.0;

    /// <summary>
    /// Computes the WCAG contrast ratio between two colors.
    /// Returns a value >= 1.0 where 1.0 means no contrast and 21.0 is maximum.
    /// </summary>
    internal static double ContrastRatio(double luminance1, double luminance2)
    {
        var lighter = Math.Max(luminance1, luminance2);
        var darker = Math.Min(luminance1, luminance2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Computes relative luminance per WCAG 2.1 definition.
    /// Input is an sRGB color with components in [0, 255].
    /// </summary>
    internal static double RelativeLuminance(int r, int g, int b)
    {
        var rLin = Linearize(r / 255.0);
        var gLin = Linearize(g / 255.0);
        var bLin = Linearize(b / 255.0);
        return 0.2126 * rLin + 0.7152 * gLin + 0.0722 * bLin;
    }

    /// <summary>
    /// Converts an sRGB component (0..1) to linear light.
    /// </summary>
    internal static double Linearize(double c) =>
        c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    /// <summary>
    /// Determines whether text is "large" per WCAG criteria.
    /// Large text is >= 18pt (24px) or bold >= 14pt (18.66px).
    /// </summary>
    internal static bool IsLargeText(string? fontSize, string? fontWeight)
    {
        var px = ParsePx(fontSize);
        if (px is null)
            return false;

        var isBold = IsBold(fontWeight);

        // 18pt = 24px, 14pt = 18.66px
        return px.Value >= 24.0 || (isBold && px.Value >= 18.66);
    }

    /// <summary>
    /// Parses a CSS color string (rgb/rgba format from computed styles) into RGB components.
    /// Returns false if the format is not recognized.
    /// </summary>
    internal static bool TryParseColor(string? color, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrWhiteSpace(color))
            return false;

        var span = color.AsSpan().Trim();

        // Handle "rgb(r, g, b)" and "rgba(r, g, b, a)"
        if (span.StartsWith("rgb"))
        {
            var open = span.IndexOf('(');
            var close = span.IndexOf(')');
            if (open < 0 || close < 0 || close <= open + 1)
                return false;

            var inner = span.Slice(open + 1, close - open - 1);
            return ParseRgbComponents(inner, out r, out g, out b);
        }

        // Handle "#RRGGBB" and "#RGB"
        if (span.Length > 0 && span[0] == '#')
        {
            return TryParseHexColor(span.Slice(1), out r, out g, out b);
        }

        return false;
    }

    private static bool ParseRgbComponents(ReadOnlySpan<char> inner, out int r, out int g, out int b)
    {
        r = g = b = 0;

        // Split by comma or space (modern CSS allows both)
        Span<Range> parts = stackalloc Range[5];
        int count;

        if (inner.Contains(','))
        {
            count = inner.Split(parts, ',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        else
        {
            count = inner.Split(parts, ' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }

        if (count < 3)
            return false;

        return int.TryParse(inner[parts[0]], NumberStyles.Integer, CultureInfo.InvariantCulture, out r) &&
               int.TryParse(inner[parts[1]], NumberStyles.Integer, CultureInfo.InvariantCulture, out g) &&
               int.TryParse(inner[parts[2]], NumberStyles.Integer, CultureInfo.InvariantCulture, out b);
    }

    private static bool TryParseHexColor(ReadOnlySpan<char> hex, out int r, out int g, out int b)
    {
        r = g = b = 0;

        if (hex.Length == 6 || hex.Length == 8)
        {
            return int.TryParse(hex.Slice(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) &&
                   int.TryParse(hex.Slice(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) &&
                   int.TryParse(hex.Slice(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        if (hex.Length == 3 || hex.Length == 4)
        {
            if (!int.TryParse(hex.Slice(0, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r) ||
                !int.TryParse(hex.Slice(1, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g) ||
                !int.TryParse(hex.Slice(2, 1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                return false;

            r = r * 16 + r;
            g = g * 16 + g;
            b = b * 16 + b;
            return true;
        }

        return false;
    }

    private static double? ParsePx(string? fontSize)
    {
        if (string.IsNullOrWhiteSpace(fontSize))
            return null;

        var span = fontSize.AsSpan().Trim();
        if (span.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            span = span.Slice(0, span.Length - 2);

        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var val)
            ? val
            : null;
    }

    private static bool IsBold(string? fontWeight)
    {
        if (string.IsNullOrWhiteSpace(fontWeight))
            return false;

        if (string.Equals(fontWeight, "bold", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fontWeight, "bolder", StringComparison.OrdinalIgnoreCase))
            return true;

        return int.TryParse(fontWeight, NumberStyles.Integer, CultureInfo.InvariantCulture, out var weight) &&
               weight >= 700;
    }
}
