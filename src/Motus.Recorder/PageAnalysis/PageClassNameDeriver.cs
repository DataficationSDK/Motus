using System.Text;

namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Converts a URL into a valid C# class name for POM generation.
/// </summary>
public static class PageClassNameDeriver
{
    /// <summary>
    /// Converts a URL to a PascalCase class name ending in "Page".
    /// Example: <c>https://example.com/login</c> becomes <c>ExampleComLoginPage</c>.
    /// </summary>
    public static string Derive(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return "UnknownPage";

        var sb = new StringBuilder();

        // Host segments (split on dots, skip "www")
        var hostParts = uri.Host.Split('.');
        foreach (var part in hostParts)
        {
            if (part.Equals("www", StringComparison.OrdinalIgnoreCase))
                continue;
            AppendPascalSegment(sb, part);
        }

        // Path segments (skip empty)
        var pathParts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in pathParts)
        {
            AppendPascalSegment(sb, part);
        }

        if (sb.Length == 0)
            return "UnknownPage";

        // Ensure first char is a letter
        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        sb.Append("Page");
        return sb.ToString();
    }

    private static void AppendPascalSegment(StringBuilder sb, string segment)
    {
        var capitalizeNext = true;
        foreach (var ch in segment)
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
    }
}
