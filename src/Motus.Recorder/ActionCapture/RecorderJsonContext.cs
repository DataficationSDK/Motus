using System.Text.Json.Serialization;

namespace Motus.Recorder.ActionCapture;

/// <summary>
/// CDP event for Page.javascriptDialogClosed (not in Motus core).
/// </summary>
internal sealed record RecorderDialogClosedEvent(bool Result, string UserInput);

/// <summary>
/// Source-generated JSON context for NativeAOT-compatible deserialization.
/// </summary>
[JsonSerializable(typeof(DomEventPayload))]
[JsonSerializable(typeof(RecorderDialogClosedEvent))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal sealed partial class RecorderJsonContext : JsonSerializerContext;
