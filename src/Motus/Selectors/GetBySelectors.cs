using System.Globalization;
using System.Text;

namespace Motus;

/// <summary>
/// Builds the selectors behind the <c>GetBy*</c> locator factories, shared by the page and frame
/// implementations so the two always resolve a factory the same way.
/// </summary>
/// <remarks>
/// An omitted <c>exact</c> means a loose match: case-insensitive, and a substring of the text is
/// enough. An exact match is case-sensitive and covers the whole text. Both collapse and trim
/// whitespace first.
/// </remarks>
internal static class GetBySelectors
{
    internal static string Role(string role, string? name)
        => RoleSelectorStrategy.BuildSelector(role, name);

    internal static string Text(string text, bool? exact)
        => $"{GetBySelectorStrategy.TextPrefix}={GetBySelectorStrategy.FormatText(text, exact ?? false)}";

    internal static string Label(string text, bool? exact)
        => $"{GetBySelectorStrategy.LabelPrefix}={GetBySelectorStrategy.FormatText(text, exact ?? false)}";

    internal static string Placeholder(string text, bool? exact)
        => Attribute("placeholder", text, exact);

    internal static string Title(string text, bool? exact)
        => Attribute("title", text, exact);

    internal static string AltText(string text, bool? exact)
        => Attribute("alt", text, exact);

    internal static string TestId(string testId)
        => $"[data-testid={CssString(testId)}]";

    private static string Attribute(string attribute, string text, bool? exact)
        => $"{GetBySelectorStrategy.AttributePrefix}={attribute}={GetBySelectorStrategy.FormatText(text, exact ?? false)}";

    /// <summary>
    /// Quotes a value as a CSS string, escaping the characters that would otherwise end the string
    /// or the selector early.
    /// </summary>
    internal static string CssString(string value)
    {
        var sb = new StringBuilder(value.Length + 2).Append('"');
        foreach (var ch in value)
        {
            if (ch is '"' or '\\')
                sb.Append('\\').Append(ch);
            else if (char.IsControl(ch))
                sb.Append('\\').Append(((int)ch).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            else
                sb.Append(ch);
        }
        return sb.Append('"').ToString();
    }
}
