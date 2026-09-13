using System.Text;

namespace Motus.Mcp;

/// <summary>
/// Renders a read of the console or request log: the entries, what the reader missed, and the
/// cursor to read from next time.
/// </summary>
/// <remarks>
/// Both logs are read the same way, so both are printed the same way. The cursor is the last line
/// rather than the first because it is what the agent needs after it has read the entries, and it
/// is printed even when there is nothing to report: a read that finds nothing still moves the
/// agent forward, and it is the read that costs least when an action says how many errors it
/// caused and the agent only wants the new ones.
/// </remarks>
internal static class LogText
{
    /// <summary>
    /// Renders one line per entry, preceded by a note when the log dropped entries the reader had
    /// not seen, and followed by the cursor line.
    /// </summary>
    /// <param name="slice">The read to render.</param>
    /// <param name="render">How to render one entry as a line.</param>
    /// <param name="nothingFound">What to say when the read returned no entries.</param>
    public static string Render<TEntry>(LogSlice<TEntry> slice, Func<TEntry, string> render, string nothingFound)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(render);

        var builder = new StringBuilder();

        if (slice.Dropped > 0)
            builder.Append(slice.Dropped).AppendLine(" earlier entries are no longer in the log.");

        if (slice.Entries.Count == 0)
        {
            builder.AppendLine(nothingFound);
        }
        else
        {
            foreach (var entry in slice.Entries)
                builder.AppendLine(render(entry));
        }

        return builder.Append("next=").Append(slice.Next).ToString();
    }
}
