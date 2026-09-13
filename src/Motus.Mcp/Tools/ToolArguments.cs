using ModelContextProtocol.Protocol;
using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// The checks every tool runs on its arguments before it touches the browser: the ones it cannot
/// work without, and the ones that have to spell a value the engine knows.
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

    /// <summary>
    /// Reads a mouse button name, defaulting to the left button when the argument is absent.
    /// Returns an error naming the value when it is none of the three, and null when it parsed.
    /// </summary>
    public static CallToolResult? Button(string? value, out MouseButton button)
    {
        switch (value?.ToLowerInvariant())
        {
            case null or "" or "left":
                button = MouseButton.Left;
                return null;
            case "right":
                button = MouseButton.Right;
                return null;
            case "middle":
                button = MouseButton.Middle;
                return null;
            default:
                button = MouseButton.Left;
                return ToolResultHelper.Error($"Unknown button '{value}'. Use left, right, or middle.");
        }
    }

    /// <summary>
    /// Reads a list of modifier key names into the flags the engine holds them in. Returns an error
    /// naming the first value it does not know, and null when every one of them parsed.
    /// </summary>
    /// <remarks>
    /// <c>None</c> is refused along with anything unknown: a caller asking for it has misread the
    /// argument as a list of every state rather than a list of keys to hold, and silently accepting
    /// it would hide that.
    /// </remarks>
    public static CallToolResult? Modifiers(string[]? values, out KeyModifier modifiers)
    {
        modifiers = KeyModifier.None;
        if (values is null)
            return null;

        foreach (var value in values)
        {
            if (!Enum.TryParse<KeyModifier>(value, ignoreCase: true, out var flag)
                || flag is KeyModifier.None
                || !Enum.IsDefined(flag))
            {
                modifiers = KeyModifier.None;
                return ToolResultHelper.Error(
                    $"Unknown modifier '{value}'. Use Alt, Control, Meta, or Shift.");
            }

            modifiers |= flag;
        }

        return null;
    }

    private static CallToolResult Required(string name)
        => ToolResultHelper.Error($"The '{name}' argument is required.");
}
