using System.ComponentModel;
using System.Text;
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
    [McpServerTool(Name = "console_messages", Title = "Read console output", Destructive = false)]
    [Description("Returns the console messages and uncaught page errors emitted since the last read, then clears "
        + "the buffer. Each line is [type] text; an uncaught error has the type pageerror.")]
    public static CallToolResult ConsoleMessages(
        ConsoleService consoleService,
        CancellationToken cancellationToken)
    {
        var entries = consoleService.Drain();
        if (entries.Count == 0)
            return ToolResultHelper.Text("No console messages have been logged since the last read.");

        var builder = new StringBuilder();
        foreach (var entry in entries)
            builder.AppendLine(entry.ToString());

        return ToolResultHelper.Text(builder.ToString().TrimEnd());
    }
}
