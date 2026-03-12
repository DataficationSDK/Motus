using System.Text;

namespace Motus.Codegen.Emit;

/// <summary>
/// Naming convention helpers for converting CDP names to C# identifiers.
/// </summary>
internal static class NamingHelper
{
    private static readonly HashSet<string> CSharpKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
        "checked", "class", "const", "continue", "decimal", "default", "delegate", "do",
        "double", "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int",
        "interface", "internal", "is", "lock", "long", "namespace", "new", "null",
        "object", "operator", "out", "override", "params", "private", "protected",
        "public", "readonly", "ref", "return", "sbyte", "sealed", "short", "sizeof",
        "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// Converts a CDP camelCase or underscore_separated name to PascalCase.
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var sb = new StringBuilder(name.Length);
        bool capitalizeNext = true;

        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (c == '_' || c == '-')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                sb.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Prefixes with @ if the name is a C# keyword.
    /// </summary>
    public static string SanitizeIdentifier(string name)
    {
        return CSharpKeywords.Contains(name) ? "@" + name : name;
    }

    /// <summary>
    /// Converts a CDP name to a safe PascalCase C# identifier.
    /// </summary>
    public static string ToSafeIdentifier(string cdpName)
    {
        return SanitizeIdentifier(ToPascalCase(cdpName));
    }

    /// <summary>
    /// Converts a CDP name to a safe camelCase C# parameter name.
    /// </summary>
    public static string ToParameterName(string cdpName)
    {
        var pascal = ToPascalCase(cdpName);
        if (pascal.Length == 0) return pascal;
        var camel = char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        return SanitizeIdentifier(camel);
    }
}
