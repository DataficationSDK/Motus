using System.Text.Json;
using System.Text.Json.Nodes;

namespace Motus.Tests.Transport;

/// <summary>
/// Keeps track of which commands a fake socket has seen and which of them are still waiting for an
/// answer, so a response a fixture writes by hand can be pointed at the command it is meant for.
/// </summary>
/// <remarks>
/// A fixture starts an action without awaiting it and then hands the socket the answer, in the
/// shape <c>var t = page.EvaluateAsync(...); socket.Enqueue(...)</c> with a literal id in the
/// response. That id is a count of the commands that happened to come before it, so one command
/// added to browser startup turns every one of those into a response the transport never matches,
/// and a fixture that stops matching waits out the command timeout rather than failing outright.
/// Pointing the response at the newest command still waiting for one says what the fixture meant:
/// answer the call I just made.
/// </remarks>
internal sealed class CdpFakeLedger
{
    private readonly object _gate = new();
    private readonly List<int> _awaiting = new();

    /// <summary>Records a command on its way out.</summary>
    internal void Sent(ReadOnlySpan<byte> command)
    {
        if (TryReadId(command, out var id))
        {
            lock (_gate)
                _awaiting.Add(id);
        }
    }

    /// <summary>
    /// Returns <paramref name="response"/> readdressed to the newest command still waiting for an
    /// answer. An event, or a response arriving when nothing is waiting, is returned unchanged.
    /// </summary>
    internal string Correlate(string response)
    {
        if (!CdpFakeResponse.CarriesId(response))
            return response;

        int id;
        lock (_gate)
        {
            if (_awaiting.Count == 0)
                return response;

            id = _awaiting[^1];
            _awaiting.RemoveAt(_awaiting.Count - 1);
        }

        return CdpFakeResponse.WithId(response, id);
    }

    /// <summary>Notes that the command with this id has been answered.</summary>
    internal void Answered(int id)
    {
        lock (_gate)
            _awaiting.Remove(id);
    }

    /// <summary>Notes that <paramref name="command"/> has been answered.</summary>
    internal void Answered(ReadOnlySpan<byte> command)
    {
        if (TryReadId(command, out var id))
            Answered(id);
    }

    /// <summary>
    /// Notes whichever command <paramref name="response"/> addresses itself, for a response that
    /// goes out exactly as the fixture wrote it.
    /// </summary>
    internal void AnsweredBy(string response)
    {
        if (JsonNode.Parse(response) is JsonObject parsed &&
            parsed["id"] is JsonValue value &&
            value.TryGetValue<int>(out var id))
        {
            Answered(id);
        }
    }

    private static bool TryReadId(ReadOnlySpan<byte> command, out int id)
    {
        id = 0;
        using var doc = JsonDocument.Parse(command.ToArray());
        if (!doc.RootElement.TryGetProperty("id", out var value) || value.ValueKind != JsonValueKind.Number)
            return false;

        id = value.GetInt32();
        return true;
    }
}
