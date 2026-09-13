using System.Text.Json;
using System.Text.Json.Nodes;

namespace Motus.Tests.Transport;

/// <summary>
/// Correlates a canned response with the command that triggered it.
/// </summary>
internal static class CdpFakeResponse
{
    /// <summary>
    /// Commands that only switch a connection-wide feature on. They return an empty result and
    /// carry nothing a fixture asserts against.
    /// </summary>
    /// <remarks>
    /// A fake answers these by method name before it looks at anything a fixture queued. That is
    /// what stops a command added to browser startup from pushing every queued response in the
    /// suite one place along.
    /// </remarks>
    private static readonly HashSet<string> Bookkeeping = new(StringComparer.Ordinal)
    {
        "Target.setDiscoverTargets",
    };

    /// <summary>
    /// Answers <paramref name="command"/> when it is one of the bookkeeping commands, with an
    /// empty result carrying the command's own id and session.
    /// </summary>
    internal static bool TryAnswerBookkeeping(ReadOnlySpan<byte> command, out string response)
    {
        response = string.Empty;

        using var doc = JsonDocument.Parse(command.ToArray());
        var root = doc.RootElement;

        if (!root.TryGetProperty("method", out var method) ||
            method.ValueKind != JsonValueKind.String ||
            !Bookkeeping.Contains(method.GetString()!))
        {
            return false;
        }

        if (!root.TryGetProperty("id", out _))
            return false;

        response = EmptyResultFor(root);
        return true;
    }

    /// <summary>
    /// Builds an empty result addressed to <paramref name="command"/>, echoing the session it was
    /// sent on when it had one.
    /// </summary>
    internal static string EmptyResultFor(JsonElement command)
    {
        var answer = new JsonObject
        {
            ["id"] = command.GetProperty("id").GetInt32(),
            ["result"] = new JsonObject(),
        };

        if (command.TryGetProperty("sessionId", out var sessionId) &&
            sessionId.ValueKind == JsonValueKind.String)
        {
            answer["sessionId"] = sessionId.GetString();
        }

        return answer.ToJsonString();
    }

    /// <summary>
    /// Returns <paramref name="response"/> with its <c>id</c> replaced by the id of
    /// <paramref name="command"/>. A response carrying no <c>id</c> is an event and is returned
    /// unchanged.
    /// </summary>
    /// <remarks>
    /// The transport correlates a response to its command on the id alone, so a queued response has
    /// to carry the right one. Fixtures write the id they expect the command to have, which means
    /// every fixture in the suite breaks the moment a command is added earlier in a sequence, and
    /// the resulting failures point at the fixture rather than at the change. Delivery order is
    /// already decided by the send that dequeues the response, so the id is bookkeeping rather than
    /// an assertion, and taking it from the command is what the fixtures were expressing anyway.
    /// Tests that mean to exercise correlation itself use <c>EnqueueRaw</c>, which does not go
    /// through here.
    /// </remarks>
    internal static string WithIdOf(ReadOnlySpan<byte> command, string response)
    {
        if (JsonNode.Parse(response) is not JsonObject parsed || !parsed.ContainsKey("id"))
            return response;

        using var doc = JsonDocument.Parse(command.ToArray());
        if (!doc.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
            return response;

        parsed["id"] = id.GetInt32();
        return parsed.ToJsonString();
    }

    /// <summary>
    /// Returns <paramref name="response"/> addressed to <paramref name="command"/>, filling the
    /// <c>id</c> in whether or not the response was written with one.
    /// </summary>
    internal static string AddressedTo(ReadOnlySpan<byte> command, string response)
    {
        using var doc = JsonDocument.Parse(command.ToArray());
        if (!doc.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
            return response;

        return WithId(response, id.GetInt32());
    }

    /// <summary>
    /// Returns <paramref name="response"/> carrying <paramref name="id"/>, replacing the one it was
    /// written with or adding it.
    /// </summary>
    internal static string WithId(string response, int id)
    {
        if (JsonNode.Parse(response) is not JsonObject parsed)
            return response;

        parsed["id"] = id;
        return parsed.ToJsonString();
    }

    /// <summary>
    /// True when <paramref name="response"/> carries an <c>id</c>, which makes it the answer to a
    /// command rather than an event.
    /// </summary>
    internal static bool CarriesId(string response)
        => JsonNode.Parse(response) is JsonObject parsed && parsed.ContainsKey("id");
}
