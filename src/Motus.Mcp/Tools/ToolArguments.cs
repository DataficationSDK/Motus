using ModelContextProtocol.Protocol;

namespace Motus.Mcp;

/// <summary>
/// The check every tool runs on the arguments it cannot work without, before it touches the
/// browser.
/// </summary>
/// <remarks>
/// Without it a missing argument surfaces wherever it first happens to be used, which for a ref is
/// a dictionary lookup reporting <c>Value cannot be null. (Parameter 'key')</c>. That names an
/// implementation detail of the server and none of the fields the caller filled in, so an agent
/// reading it has no way to tell which argument to fix.
/// </remarks>
internal static class ToolArguments
{
    /// <summary>
    /// Returns an error naming the argument when it is missing or blank, and null when it is
    /// usable, so a tool reads as <c>if (ToolArguments.Missing("ref", @ref) is { } error) return error;</c>.
    /// </summary>
    /// <remarks>
    /// Whitespace counts as missing: every argument checked here names something (an element, a
    /// URL, a key, a context) and a run of spaces names none of them.
    /// </remarks>
    public static CallToolResult? Missing(string name, string? value)
        => string.IsNullOrWhiteSpace(value) ? Required(name) : null;

    /// <summary>
    /// The same check for an argument that takes several values. An empty list is missing: a call
    /// asking for nothing to be selected or uploaded has left the argument out rather than meant it.
    /// </summary>
    public static CallToolResult? Missing(string name, string[]? values)
        => values is null || values.Length == 0 ? Required(name) : null;

    /// <summary>
    /// The check for an argument carrying free text rather than naming something: the value is
    /// required, but whitespace is a value like any other. Typing a space into a field is a thing
    /// a caller can mean, so only a missing argument is refused here.
    /// </summary>
    public static CallToolResult? Unset(string name, string? value)
        => value is null ? Required(name) : null;

    private static CallToolResult Required(string name)
        => ToolResultHelper.Error($"The '{name}' argument is required.");
}
