using System.Text.Json;
using System.Text.Json.Serialization;

namespace Motus;

/// <summary>
/// Outbound BiDi command envelope sent over the WebSocket.
/// </summary>
internal sealed record BiDiCommandEnvelope(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("params")] JsonElement Params);

/// <summary>
/// Inbound BiDi message discriminator. BiDi messages are discriminated by the
/// <c>type</c> field: <c>"success"</c>, <c>"error"</c>, or <c>"event"</c>.
/// </summary>
internal sealed record BiDiInboundDiscriminator(
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("id")] int? Id,
    [property: JsonPropertyName("method")] string? Method,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("result")] JsonElement? Result,
    [property: JsonPropertyName("params")] JsonElement? Params);

/// <summary>
/// Raw event payload surfaced to BiDi event channels before typed deserialization.
/// </summary>
internal readonly record struct RawBiDiEvent(JsonElement Params, string ContextId);
