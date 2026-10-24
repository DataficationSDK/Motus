using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Motus.Runner.Services.Timeline;

namespace Motus.Runner.Services.Execution;

/// <summary>The kinds of <see cref="TestHostMessage"/> a runner and its test host exchange.</summary>
internal static class TestHostMessageTypes
{
    // Runner to test host. Each request carries an id that its reply repeats.
    public const string Discover = "discover";
    public const string Initialize = "initialize";
    public const string Run = "run";
    public const string Cancel = "cancel";
    public const string Cleanup = "cleanup";
    public const string StepContinue = "step-continue";

    // Test host to runner: replies.
    public const string Discovered = "discovered";
    public const string Initialized = "initialized";
    public const string Result = "result";
    public const string CleanedUp = "cleaned-up";

    // Test host to runner: notices, with no reply, except Step, which waits for StepContinue.
    public const string Log = "log";
    public const string Frame = "frame";
    public const string Timeline = "timeline";
    public const string Step = "step";
}

/// <summary>
/// One message between a runner and a test host process. Only the members its
/// <see cref="Type"/> uses are set.
/// </summary>
internal sealed class TestHostMessage
{
    public string Type { get; set; } = "";

    /// <summary>Pairs a request with its reply, and a step notice with its continue.</summary>
    public int Id { get; set; }

    /// <summary>The test case a run names, or the name filter for discovery.</summary>
    public string? Name { get; set; }

    /// <summary>A log line, a screencast frame (base64 JPEG), or the action a step paused before.</summary>
    public string? Text { get; set; }

    /// <summary>The selector a paused step acts on.</summary>
    public string? Selector { get; set; }

    public string? AssemblyName { get; set; }

    public List<TestCaseInfo>? Tests { get; set; }

    public TestCaseReport? Report { get; set; }

    public TestHostFailure? Failure { get; set; }

    public TimelineEntry? Entry { get; set; }
}

/// <summary>
/// Reads and writes <see cref="TestHostMessage"/>s on a stream, one JSON object per line. Writes
/// are queued and sent in the order they were posted, so a notice posted before a reply arrives
/// before it.
/// </summary>
internal sealed class TestHostConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Stream _stream;
    private readonly StreamReader _reader;
    private readonly Channel<TestHostMessage> _outgoing = Channel.CreateUnbounded<TestHostMessage>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly Task _writer;

    public TestHostConnection(Stream stream)
    {
        _stream = stream;
        _reader = new StreamReader(stream, s_utf8, detectEncodingFromByteOrderMarks: false, bufferSize: 64 * 1024, leaveOpen: true);
        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>Queues <paramref name="message"/> to be sent. Does nothing once the connection has closed.</summary>
    public void Post(TestHostMessage message) => _outgoing.Writer.TryWrite(message);

    /// <summary>The next message, or null when the other side has closed the connection.</summary>
    public async Task<TestHostMessage?> ReadAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            string? line;
            try
            {
                line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (IOException)
            {
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }

            if (line is null)
                return null;
            if (line.Length == 0)
                continue;

            return JsonSerializer.Deserialize<TestHostMessage>(line, s_options);
        }
    }

    private async Task WriteLoopAsync()
    {
        try
        {
            await foreach (var message in _outgoing.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(message, s_options);
                await _stream.WriteAsync(bytes).ConfigureAwait(false);
                await _stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
                await _stream.FlushAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // The other side has gone; the read side reports it.
        }
        finally
        {
            _outgoing.Writer.TryComplete();
        }
    }

    /// <summary>Sends what is queued, then closes the stream.</summary>
    public async ValueTask DisposeAsync()
    {
        _outgoing.Writer.TryComplete();
        try { await _writer.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false); }
        catch { /* the stream is closed below either way */ }

        _reader.Dispose();
        await _stream.DisposeAsync().ConfigureAwait(false);
    }
}
