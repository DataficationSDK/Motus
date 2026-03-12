using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motus;

/// <summary>
/// Outbound CDP command envelope sent over the WebSocket.
/// </summary>
internal sealed record CdpCommandEnvelope(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement Params,
    [property: JsonPropertyName("sessionId")] string? SessionId = null);

/// <summary>
/// Inbound CDP message envelope. Discriminated by presence of <see cref="Id"/> (response)
/// vs <see cref="Method"/> without Id (event).
/// </summary>
internal sealed record CdpInboundEnvelope(
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("result")] JsonElement? Result,
    [property: JsonPropertyName("error")] CdpErrorPayload? Error,
    [property: JsonPropertyName("params")] JsonElement? Params,
    [property: JsonPropertyName("sessionId")] string? SessionId);

/// <summary>
/// CDP error payload returned inside an inbound response envelope.
/// </summary>
internal sealed record CdpErrorPayload(
    [property: JsonPropertyName("code")] int Code,
    [property: JsonPropertyName("message")] string Message);
