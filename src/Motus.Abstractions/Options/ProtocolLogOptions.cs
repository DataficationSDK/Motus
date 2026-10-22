namespace Motus.Abstractions;

/// <summary>
/// Configuration for the protocol log, which records every CDP or WebDriver BiDi message Motus
/// sends to the browser and every message the browser sends back. Disabled by default.
/// </summary>
/// <remarks>
/// <para>
/// The log is a debugging aid for questions such as whether a fault lies in Motus or in the
/// browser. Each message becomes one line carrying a timestamp, a connection tag, the direction,
/// the command id, the session, the method and the JSON payload. Responses also carry how long the
/// command took.
/// </para>
/// <para>
/// With <see cref="RedactSecrets"/> on, authorization and cookie headers, cookie values, password
/// fields and request bodies are replaced before a line is written. Typed keystrokes, filled
/// values, script arguments and URLs are logged as they are, because hiding them would hide most of
/// what the log is for. Treat a log as sensitive: it can hold whatever a test typed.
/// </para>
/// <para>
/// The <c>MOTUS_PROTOCOL_LOG</c> environment variable turns the log on without a code change. It is
/// read only when no options are set here, or when the options set here name no destination.
/// </para>
/// </remarks>
public sealed record ProtocolLogOptions
{
    /// <summary>
    /// A file to append the log to, or <c>"stderr"</c> to write it to the standard error stream.
    /// Standard output is refused, because it carries the MCP server's JSON-RPC channel.
    /// </summary>
    /// <remarks>
    /// The file is opened for appending and each process starts its part with a header naming its
    /// process id, so several test processes can share one path. On Unix a new file is readable by
    /// its owner only. Browsers in one process that log to the same path share one file, and each
    /// line is tagged with the connection it belongs to.
    /// </remarks>
    public string? Path { get; init; }

    /// <summary>
    /// Receives each formatted line, without a trailing newline. It is called from a background
    /// task, one line at a time, never from the thread that sent or received the message.
    /// </summary>
    /// <remarks>
    /// May be set with <see cref="Path"/>, in which case both receive every line. An exception it
    /// throws is reported once on the standard error stream and does not stop the log. Bridging to
    /// an <c>ILogger</c> takes one line: <c>Sink = line =&gt; logger.LogTrace("{Line}", line)</c>.
    /// </remarks>
    public Action<string>? Sink { get; init; }

    /// <summary>
    /// The longest a JSON string value may be before it is trimmed, in characters. Default: 1024.
    /// Zero or less turns trimming off.
    /// </summary>
    /// <remarks>
    /// A trimmed value keeps its first characters and ends with a count of what was cut, so the
    /// payload stays valid JSON. This is what keeps screenshots, trace chunks, response bodies and
    /// injected scripts from flooding the log.
    /// </remarks>
    public int MaxValueLength { get; init; } = 1024;

    /// <summary>
    /// Whether values that are rarely needed to debug a protocol exchange and often secret are
    /// replaced before a line is written. Default: true.
    /// </summary>
    /// <remarks>
    /// Covers authorization, cookie and token-like headers in both object and array form, raw
    /// header text, cookie values, any property named <c>password</c>, and request bodies. Typed
    /// text, filled values and script arguments are never redacted. There is no environment
    /// variable for this setting, so logging secrets takes a deliberate change to code.
    /// </remarks>
    public bool RedactSecrets { get; init; } = true;
}
