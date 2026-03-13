using System.Globalization;
using System.Text;

namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Derives PascalCase C# property names from element attributes.
/// </summary>
internal static class MemberNameDeriver
{
    /// <summary>
    /// Derives a unique C# member name for each element, appending type suffixes
    /// and numeric disambiguators as needed.
    /// </summary>
    internal static IReadOnlyList<string> DeriveNames(IReadOnlyList<PageElementInfo> elements)
    {
        var names = new string[elements.Count];
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < elements.Count; i++)
        {
            var baseName = DeriveBaseName(elements[i]);
            var suffix = GetTypeSuffix(elements[i]);
            var candidate = baseName + suffix;

            if (seen.TryGetValue(candidate, out var count))
            {
                seen[candidate] = count + 1;
                candidate += (count + 1).ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                seen[candidate] = 1;
            }

            names[i] = candidate;
        }

        return names;
    }

    internal static string DeriveBaseName(PageElementInfo info)
    {
        var raw = FirstNonEmpty(
            info.Id,
            info.Name,
            info.AriaLabel,
            info.Placeholder,
            TruncateText(info.Text, 30));

        if (raw is not null)
            return ToPascalCase(raw);

        return $"Element{info.ElementIndex}";
    }

    internal static string GetTypeSuffix(PageElementInfo info)
    {
        var tag = info.Tag.ToLowerInvariant();
        var type = info.Type?.ToLowerInvariant();

        return tag switch
        {
            "select" => "Dropdown",
            "a" => "Link",
            "button" => "Button",
            "input" when type is "checkbox" => "Checkbox",
            "input" when type is "radio" => "Radio",
            "input" when type is "submit" or "button" or "reset" or "image" => "Button",
            "input" => "Input",
            _ when info.Role?.Equals("button", StringComparison.OrdinalIgnoreCase) == true => "Button",
            _ => "Element"
        };
    }

    internal static string ToPascalCase(string input)
    {
        var sb = new StringBuilder(input.Length);
        var capitalizeNext = true;

        foreach (var ch in input)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(capitalizeNext ? char.ToUpperInvariant(ch) : ch);
                capitalizeNext = false;
            }
            else
            {
                capitalizeNext = true;
            }
        }

        if (sb.Length == 0)
            return "Element";

        // Ensure first char is a letter
        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        return sb.ToString();
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
                return v;
        }
        return null;
    }

    private static string? TruncateText(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var trimmed = text.Trim();
        if (trimmed.Length <= maxLength)
            return trimmed;

        // Truncate at last word boundary
        var cutoff = trimmed.LastIndexOf(' ', maxLength);
        return cutoff > 0 ? trimmed[..cutoff] : trimmed[..maxLength];
    }
}
