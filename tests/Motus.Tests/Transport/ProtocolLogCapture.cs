using System.Collections.Concurrent;
using System.Diagnostics;
using Motus.Abstractions;

namespace Motus.Tests.Transport;

/// <summary>
/// Collects protocol log lines through a callback sink, so a test can read what a transport logged
/// without a file.
/// </summary>
internal sealed class ProtocolLogCapture
{
    private readonly ConcurrentQueue<string> _lines = new();

    /// <summary>Options that send every line here.</summary>
    internal ProtocolLogOptions Options => new() { Sink = _lines.Enqueue };

    /// <summary>Opens a log for a test transport that sends every line here.</summary>
    internal ProtocolLog Open(ProtocolLogOptions? options = null)
        => ProtocolLog.Open((options ?? new ProtocolLogOptions()) with { Sink = _lines.Enqueue }, "cdp-ws", "test:1")!;

    /// <summary>Every line so far, with its timestamp and connection tag taken off.</summary>
    internal IReadOnlyList<string> Lines => _lines.Select(Body).ToArray();

    /// <summary>Every line so far, as written.</summary>
    internal IReadOnlyList<string> RawLines => _lines.ToArray();

    /// <summary>
    /// Waits for the background writer to deliver a line matching <paramref name="predicate"/>.
    /// </summary>
    internal async Task<IReadOnlyList<string>> WaitForAsync(Func<string, bool> predicate)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(5))
        {
            var lines = Lines;
            if (lines.Any(predicate))
                return lines;

            await Task.Delay(10);
        }

        Assert.Fail("Expected protocol log line never arrived. Lines:\n" + string.Join("\n", Lines));
        return Array.Empty<string>();
    }

    /// <summary>Takes the timestamp and connection tag off a line.</summary>
    internal static string Body(string line)
    {
        var first = line.IndexOf(' ');
        var second = line.IndexOf(' ', first + 1);
        return line[(second + 1)..];
    }
}
