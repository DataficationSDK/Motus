using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Channels;

namespace Motus;

/// <summary>
/// One destination for protocol log lines: a file, the standard error stream, or a caller's
/// callback.
/// </summary>
/// <remarks>
/// <para>
/// Lines are queued by whoever logs them and written by a task of the sink's own, so neither the
/// send path nor the receive loop of a transport ever waits on a disk or on a caller's code. The
/// writer flushes each time the queue runs dry, which keeps a file current to within moments and
/// means a process that hangs or is killed loses almost nothing. A file gets everything queued since
/// the last flush in a single append, so the lines of one flush stay together even when other
/// writers share the file.
/// </para>
/// <para>
/// File and standard error sinks are shared across the process and counted: every transport that
/// logs to the same place holds a reference, and the last one to let go drains the queue and
/// closes the file. A callback sink belongs to the one transport it was made for.
/// </para>
/// </remarks>
internal sealed class ProtocolLogSink
{
    private static readonly object s_gate = new();

    private static readonly Dictionary<string, ProtocolLogSink> s_shared = new(
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal);

    private const string StandardErrorKey = "\0stderr";

    /// <summary>How much a file sink gathers before it appends, even with more lines queued.</summary>
    private const int MaxPendingBytes = 256 * 1024;

    private readonly string? _key;
    private readonly ProtocolLogFile? _file;
    private readonly ArrayBufferWriter<byte>? _pending;
    private readonly bool _toStandardError;
    private readonly Action<string>? _callback;
    private readonly TextWriter _warnings;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly Task _consumer;
    private int _references = 1;
    private bool _reportedFailure;

    private ProtocolLogSink(
        string? key, ProtocolLogFile? file, bool toStandardError, Action<string>? callback, TextWriter warnings)
    {
        _key = key;
        _file = file;
        _pending = file is null ? null : new ArrayBufferWriter<byte>(4096);
        _toStandardError = toStandardError;
        _callback = callback;
        _warnings = warnings;
        _consumer = Task.Run(ConsumeAsync);
    }

    /// <summary>
    /// Takes a reference to the shared sink for a file, opening the file when nobody else has it
    /// open, or to the shared standard error sink when <paramref name="path"/> names it.
    /// </summary>
    /// <returns>
    /// The sink, or <see langword="null"/> when the file could not be opened. That is reported on
    /// <paramref name="warnings"/> and never thrown: a launch does not fail because its log could not.
    /// </returns>
    internal static ProtocolLogSink? Acquire(string path, TextWriter? warnings = null)
    {
        warnings ??= Console.Error;

        string key;
        string? fullPath = null;
        if (ProtocolLogSettings.IsStandardError(path))
        {
            key = StandardErrorKey;
        }
        else
        {
            try
            {
                fullPath = System.IO.Path.GetFullPath(path);
            }
            catch (Exception ex)
            {
                warnings.WriteLine($"Motus: the protocol log could not open '{path}' ({ex.Message}); logging is off.");
                return null;
            }

            key = fullPath;
        }

        lock (s_gate)
        {
            if (s_shared.TryGetValue(key, out var existing))
            {
                existing._references++;
                return existing;
            }

            ProtocolLogSink sink;
            if (fullPath is null)
            {
                sink = new ProtocolLogSink(key, file: null, toStandardError: true, callback: null, warnings);
            }
            else
            {
                var file = TryOpenFile(fullPath, warnings);
                if (file is null)
                    return null;

                sink = new ProtocolLogSink(key, file, toStandardError: false, callback: null, warnings);
            }

            s_shared[key] = sink;
            return sink;
        }
    }

    /// <summary>
    /// Makes a sink that hands each line to <paramref name="callback"/>. It is not shared.
    /// </summary>
    internal static ProtocolLogSink ForCallback(Action<string> callback, TextWriter? warnings = null)
        => new(key: null, file: null, toStandardError: false, callback, warnings ?? Console.Error);

    /// <summary>
    /// Queues one line. Never blocks and never throws.
    /// </summary>
    internal void Write(string line) => _queue.Writer.TryWrite(line);

    /// <summary>
    /// Lets go of one reference. The last one to let go waits for every queued line to be written
    /// and then closes the file.
    /// </summary>
    internal async ValueTask ReleaseAsync()
    {
        lock (s_gate)
        {
            if (--_references > 0)
                return;

            if (_key is not null)
                s_shared.Remove(_key);
        }

        _queue.Writer.TryComplete();

        try { await _consumer.ConfigureAwait(false); }
        catch { /* The consumer reports its own failures. */ }

        if (_file is not null)
        {
            try { _file.Dispose(); }
            catch { /* Nothing left to report it to. */ }
        }
    }

    /// <summary>Whether a file is held open as a shared sink, for tests.</summary>
    internal static bool IsOpen(string path)
    {
        var key = System.IO.Path.GetFullPath(path);
        lock (s_gate) return s_shared.ContainsKey(key);
    }

    private static ProtocolLogFile? TryOpenFile(string fullPath, TextWriter warnings)
    {
        ProtocolLogFile? file = null;
        try
        {
            file = ProtocolLogFile.Open(fullPath);

            // Appending rather than truncating lets several processes share a path, and the header
            // tells their parts apart.
            file.Append(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
                $"# motus protocol log, pid {Environment.ProcessId}, {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ}\n")));
            return file;
        }
        catch (Exception ex)
        {
            file?.Dispose();
            warnings.WriteLine($"Motus: the protocol log could not open '{fullPath}' ({ex.Message}); logging is off.");
            return null;
        }
    }

    private async Task ConsumeAsync()
    {
        var reader = _queue.Reader;
        while (await reader.WaitToReadAsync().ConfigureAwait(false))
        {
            while (reader.TryRead(out var line))
            {
                Emit(line);
                if (_pending is { WrittenCount: >= MaxPendingBytes })
                    Flush();
            }

            Flush();
        }
    }

    private void Emit(string line)
    {
        try
        {
            if (_callback is not null)
            {
                _callback(line);
            }
            else if (_toStandardError)
            {
                // One write per line, so a line is never split by something else writing to
                // standard error at the same moment.
                Console.Error.Write(line + "\n");
            }
            else
            {
                Encoding.UTF8.GetBytes(line, _pending!);
                _pending.Write("\n"u8);
            }
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
    }

    private void Flush()
    {
        try
        {
            if (_file is not null)
                _file.Append(_pending!.WrittenSpan);
            else if (_toStandardError)
                Console.Error.Flush();
        }
        catch (Exception ex)
        {
            ReportFailure(ex);
        }
        finally
        {
            // Dropped on failure too, so a file that cannot be written does not hold every later line.
            _pending?.ResetWrittenCount();
        }
    }

    private void ReportFailure(Exception ex)
    {
        // Reported once, since a sink that fails usually fails on every line after.
        if (_reportedFailure)
            return;

        _reportedFailure = true;

        try
        {
            var what = _callback is not null ? "a protocol log sink threw" : "writing the protocol log failed";
            _warnings.WriteLine($"Motus: {what} ({ex.Message}); later failures are not reported.");
        }
        catch
        {
            Debug.WriteLine(ex);
        }
    }
}
