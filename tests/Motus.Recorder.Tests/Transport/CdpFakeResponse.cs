using System.Text.Json;
using System.Text.Json.Nodes;

namespace Motus.Recorder.Tests.Transport;

/// <summary>
/// Correlates a canned response with the command that triggered it.
/// </summary>
internal static class CdpFakeResponse
{
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
    /// Tests that mean to exercise correlation itself use <c>Enqueue</c>, which does not go through
    /// here.
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
}
