using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Motus.Mcp;

/// <summary>
/// Builds the small set of tool result shapes the tools return: a plain text
/// status, an error status the model can act on, an inline image, and a structured
/// value paired with its text rendering.
/// </summary>
internal static class ToolResultHelper
{
    /// <summary>A successful result carrying a single line of status text.</summary>
    public static CallToolResult Text(string text)
        => new() { Content = [new TextContentBlock { Text = text }] };

    /// <summary>
    /// A failed result. The message is surfaced to the model (rather than thrown as
    /// a protocol error) so it can read the guidance and retry.
    /// </summary>
    public static CallToolResult Error(string message)
        => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    /// <summary>A successful result carrying an inline image.</summary>
    public static CallToolResult Image(byte[] bytes, string mimeType = "image/png")
        => new() { Content = [ImageContentBlock.FromBytes(bytes, mimeType)] };

    /// <summary>
    /// A successful result carrying a structured value. The value is set as the
    /// machine-readable structured content, and a text rendering is included for
    /// readability (the JSON itself when no text is supplied).
    /// </summary>
    public static CallToolResult Structured(JsonElement value, string? text = null)
        => new()
        {
            StructuredContent = value,
            Content = [new TextContentBlock { Text = text ?? value.ToString() }],
        };

    /// <summary>
    /// The guidance returned when a ref is used before any snapshot has been taken.
    /// Shared by every tool that addresses an element by ref.
    /// </summary>
    public static CallToolResult NoSnapshot()
        => Error("No snapshot has been taken. Call snapshot first, then retry with a ref from it.");

    /// <summary>
    /// The guidance returned when a ref is not in the latest snapshot. Shared by
    /// every tool that addresses an element by ref.
    /// </summary>
    public static CallToolResult Stale(StaleRefException ex)
        => Error($"Ref '{ex.RefId}' is not in the latest snapshot. Call snapshot to refresh refs, then retry.");
}
