using System.Text;

namespace Motus;

/// <summary>
/// Reads a launched browser's output streams continuously and keeps the tail of what it wrote.
/// </summary>
/// <remarks>
/// A browser's output is redirected so its diagnostics stay out of the host program's own, and a
/// redirected stream nobody reads is a pipe that fills. Once it is full the browser blocks on its
/// next write, which from the outside is indistinguishable from a browser that has stopped
/// answering: commands stop being served, the heartbeat gives up, and the process never leaves on
/// its own. Reading the streams as they are written keeps the pipe empty.
///
/// What the browser wrote is worth keeping rather than discarding, because when a launch fails it
/// is the only account of why.
/// </remarks>
internal sealed class BrowserOutputDrain
{
    /// <summary>
    /// How much of the tail is kept: room for a run of browser errors, small enough to carry in a
    /// message. A browser that writes more than this has already said what matters at the end.
    /// </summary>
    private const int RetainedCharacters = 8 * 1024;

    private const int ReadChunkCharacters = 4 * 1024;

    private readonly StringBuilder _tail = new();

    private BrowserOutputDrain()
    {
    }

    /// <summary>
    /// Starts draining the given streams. Each reader ends on its own when the browser exits and
    /// its streams close, so there is nothing to stop or dispose.
    /// </summary>
    internal static BrowserOutputDrain Start(params TextReader[] streams)
    {
        var drain = new BrowserOutputDrain();

        foreach (var stream in streams)
            _ = drain.ReadAsync(stream);

        return drain;
    }

    /// <summary>
    /// The last of what the browser wrote, oldest first, across all drained streams.
    /// </summary>
    internal string Tail
    {
        get
        {
            lock (_tail)
                return _tail.ToString();
        }
    }

    /// <summary>
    /// Renders the retained output for an error message, or nothing at all when the browser was silent.
    /// </summary>
    internal string Describe()
    {
        var tail = Tail;

        return tail.Length == 0
            ? string.Empty
            : $"{Environment.NewLine}The browser wrote:{Environment.NewLine}{tail}";
    }

    private async Task ReadAsync(TextReader stream)
    {
        var buffer = new char[ReadChunkCharacters];

        try
        {
            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory()).ConfigureAwait(false)) > 0)
                Append(buffer.AsSpan(0, read));
        }
        catch (Exception)
        {
            // The stream goes away with the browser, whether it exited or was killed. Draining is
            // housekeeping, so its end is never a failure the caller needs to hear about.
        }
    }

    private void Append(ReadOnlySpan<char> text)
    {
        lock (_tail)
        {
            _tail.Append(text);

            if (_tail.Length > RetainedCharacters)
                _tail.Remove(0, _tail.Length - RetainedCharacters);
        }
    }
}
