using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Motus.Mcp;

/// <summary>
/// Tool for reading the console output and uncaught errors the active tab has
/// emitted.
/// </summary>
[McpServerToolType]
public sealed class ConsoleTools
{
    [McpServerTool(Name = "console_messages", Title = "Read console output", Destructive = false, ReadOnly = true)]
    [Description("Returns the console messages and uncaught page errors the active tab has logged. Each line is "
        + "[type] text; an uncaught error has the type pageerror. Reading does not clear the log, so the same "
        + "call can be made again. The last line is next=N: pass that as since to read only what arrives after "
        + "this read. An action result that counts errors gives the since value that returns exactly those.")]
    public static CallToolResult ConsoleMessages(
        ConsoleService consoleService,
        CancellationToken cancellationToken,
        [Description("Return only entries from this sequence number onwards. Omit to read everything the log holds.")]
        long? since = null)
    {
        var slice = consoleService.Read(since);
        return ToolResultHelper.Text(LogText.Render(
            slice,
            entry => entry.ToString(),
            since is null
                ? "No console messages have been logged."
                : $"No console messages have been logged since {since}."));
    }
}
