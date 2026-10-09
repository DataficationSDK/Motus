using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// The protocol log of one connection to a browser. A transport holds one when logging is on and
/// none at all when it is off, so an unlogged connection pays a null check per message and nothing
/// more.
/// </summary>
/// <remarks>
/// <para>
/// Every line starts with a UTC timestamp and a connection tag such as <c>c1</c>, which tells the
/// lines of several browsers sharing one file apart. Then comes one of:
/// </para>
/// <list type="bullet">
/// <item><c>-&gt; #42 &lt;session&gt; Method {params}</c> for a command sent.</item>
/// <item><c>&lt;- #42 &lt;session&gt; Method 2.1ms {result}</c> for its response, or
/// <c>error &lt;code&gt; "message"</c> in place of the result.</item>
/// <item><c>&lt;- event &lt;session&gt; Method {params}</c> for an event, whether anything was
/// listening for it or not.</item>
/// <item><c>xx #42 &lt;session&gt; Method cancelled after 60000.0ms</c> for a command that ended
/// without an answer.</item>
/// <item><c>open</c>, <c>closed</c> and <c>&lt;- malformed</c> for the connection itself and for
/// messages that were not JSON.</item>
/// </list>
/// <para>
/// The session is shown as <c>-</c> for the browser itself, and always for BiDi, whose envelope
/// carries no session.
/// </para>
/// </remarks>
internal sealed class ProtocolLog : IAsyncDisposable
{
    private static int s_nextConnection;

    private readonly ProtocolLogSink[] _sinks;
    private readonly int _maxValueLength;
    private readonly bool _redactSecrets;
    private readonly ConcurrentDictionary<int, PendingCommand> _pending = new();
    private int _closed;
    private int _disposed;

    /// <summary>The tag that marks this connection's lines.</summary>
    internal string Tag { get; }

    private ProtocolLog(ProtocolLogSink[] sinks, ProtocolLogOptions options)
    {
        _sinks = sinks;
        _maxValueLength = options.MaxValueLength;
        _redactSecrets = options.RedactSecrets;
        Tag = "c" + Interlocked.Increment(ref s_nextConnection).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Opens a log for one connection, if the settings ask for one.
    /// </summary>
    /// <param name="options">Resolved options, from <see cref="ProtocolLogSettings.Resolve"/>.</param>
    /// <param name="kind">The kind of connection: <c>cdp-pipe</c>, <c>cdp-ws</c> or <c>bidi-ws</c>.</param>
    /// <param name="describe">What the connection is to, kept free of anything that grants control of the browser.</param>
    /// <param name="warnings">Where a destination that cannot be opened is reported. Standard error by default.</param>
    /// <returns>The log, or <see langword="null"/> when logging is off or no destination could be opened.</returns>
    internal static ProtocolLog? Open(
        ProtocolLogOptions? options, string kind, string describe, TextWriter? warnings = null)
    {
        if (options is null)
            return null;

        var sinks = new List<ProtocolLogSink>(2);

        if (options.Path is { } path && ProtocolLogSink.Acquire(path, warnings) is { } shared)
            sinks.Add(shared);

        if (options.Sink is { } callback)
            sinks.Add(ProtocolLogSink.ForCallback(callback, warnings));

        if (sinks.Count == 0)
            return null;

        var log = new ProtocolLog(sinks.ToArray(), options);
        log.Write($"open {kind} {describe}");
        return log;
    }

    /// <summary>
    /// Describes a WebSocket endpoint by host and port alone. The path of a browser's endpoint is
    /// what lets anyone drive it, so it is left out.
    /// </summary>
    internal static string DescribeEndpoint(Uri endpoint)
        => endpoint.IsDefaultPort ? endpoint.Host : $"{endpoint.Host}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Formats a command's params. Done before the send lock is taken, so senders do not queue up
    /// behind each other's formatting.
    /// </summary>
    internal string FormatCommand(string method, JsonElement paramsElement)
    {
        try
        {
            var formatted = ProtocolLogFormatter.Format(paramsElement, method, _maxValueLength, _redactSecrets);
            return formatted.Length == 0 ? "{}" : formatted;
        }
        catch (Exception ex)
        {
            return $"<payload could not be formatted: {ex.Message}>";
        }
    }

    /// <summary>
    /// Logs a command. Called under the send lock and immediately before the bytes go out, so the
    /// order of these lines is the order on the wire, and a response can never be logged ahead of
    /// the command it answers.
    /// </summary>
    internal void CommandSent(int id, string? sessionId, string method, string payload)
    {
        try
        {
            _pending[id] = new PendingCommand(method, sessionId, Stopwatch.GetTimestamp());
            Write($"-> #{Id(id)} {Session(sessionId)} {method} {payload}");
        }
        catch
        {
            // A log that fails must never take the connection with it.
        }
    }

    /// <summary>
    /// Logs a command that ended without its response being logged: cancelled, timed out, or cut
    /// off by the connection closing. Does nothing when the response was logged.
    /// </summary>
    internal void CommandEnded(int id, string reason)
    {
        try
        {
            if (!_pending.TryRemove(id, out var command))
                return;

            Write($"xx #{Id(id)} {Session(command.SessionId)} {command.Method} {reason} after {Elapsed(command)}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// Logs an inbound CDP message, before it is dispatched, so lines keep the order in which the
    /// browser sent them.
    /// </summary>
    internal void Received(CdpInboundEnvelope envelope, ReadOnlySpan<byte> raw)
    {
        try
        {
            if (envelope.Id is { } id)
            {
                if (envelope.Error is { } error)
                {
                    LogResponse(id, envelope.SessionId,
                        $"error {error.Code.ToString(CultureInfo.InvariantCulture)} {ProtocolLogFormatter.Quote(error.Message, _maxValueLength)}");
                }
                else
                {
                    LogResponse(id, envelope.SessionId, result: envelope.Result);
                }
            }
            else if (envelope.Method is { } method)
            {
                LogEvent(envelope.SessionId, method, envelope.Params);
            }
            else
            {
                LogOther(raw);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Logs an inbound BiDi message, before it is dispatched.
    /// </summary>
    internal void Received(BiDiInboundDiscriminator message, ReadOnlySpan<byte> raw)
    {
        try
        {
            if (message.Type is "success" or "error" && message.Id is { } id)
            {
                if (message.Type == "error")
                {
                    LogResponse(id, sessionId: null,
                        $"error {message.Error ?? "unknown error"} {ProtocolLogFormatter.Quote(message.Message ?? string.Empty, _maxValueLength)}");
                }
                else
                {
                    LogResponse(id, sessionId: null, result: message.Result);
                }
            }
            else if (message.Type == "event" && message.Method is { } method)
            {
                LogEvent(sessionId: null, method, message.Params);
            }
            else
            {
                LogOther(raw);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// Logs a message that was not valid JSON, which the transport then drops.
    /// </summary>
    internal void Malformed(ReadOnlySpan<byte> raw)
    {
        try
        {
            Write($"<- malformed {ProtocolLogFormatter.FormatMalformed(raw, _maxValueLength)}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// Logs the end of the connection. Only the first call writes, since a connection can be seen
    /// to close from more than one place.
    /// </summary>
    internal void Closed(string reason)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
            return;

        try
        {
            Write($"closed {reason}");
        }
        catch
        {
        }
    }

    /// <summary>
    /// Lets go of the destinations. Once the last connection logging to a file lets go, every line
    /// queued for it has been written and the file is closed.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        foreach (var sink in _sinks)
            await sink.ReleaseAsync().ConfigureAwait(false);
    }

    private void LogResponse(int id, string? sessionId, string outcome)
    {
        if (_pending.TryRemove(id, out var command))
            Write($"<- #{Id(id)} {Session(command.SessionId)} {command.Method} {Elapsed(command)} {outcome}");
        else
            Write($"<- #{Id(id)} {Session(sessionId)} (no pending command) {outcome}");
    }

    private void LogResponse(int id, string? sessionId, JsonElement? result)
    {
        if (_pending.TryRemove(id, out var command))
        {
            var payload = Format(result, command.Method);
            Write($"<- #{Id(id)} {Session(command.SessionId)} {command.Method} {Elapsed(command)} {payload}");
        }
        else
        {
            Write($"<- #{Id(id)} {Session(sessionId)} (no pending command) {Format(result, method: null)}");
        }
    }

    private void LogEvent(string? sessionId, string method, JsonElement? parameters)
        => Write($"<- event {Session(sessionId)} {method} {Format(parameters, method)}");

    private void LogOther(ReadOnlySpan<byte> raw)
    {
        using var document = JsonDocument.Parse(raw.ToArray());
        Write($"<- other {Format(document.RootElement, method: null)}");
    }

    private string Format(JsonElement? element, string? method)
        => element is { } value
            ? ProtocolLogFormatter.Format(value, method, _maxValueLength, _redactSecrets)
            : "{}";

    private void Write(string entry)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        var line = string.Create(CultureInfo.InvariantCulture, $"{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ss.fffZ} {Tag} {entry}");
        foreach (var sink in _sinks)
            sink.Write(line);
    }

    private static string Id(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Session(string? sessionId) => string.IsNullOrEmpty(sessionId) ? "-" : sessionId;

    private static string Elapsed(PendingCommand command)
        => Stopwatch.GetElapsedTime(command.StartTimestamp).TotalMilliseconds.ToString("0.0", CultureInfo.InvariantCulture) + "ms";

    private readonly record struct PendingCommand(string Method, string? SessionId, long StartTimestamp);
}
