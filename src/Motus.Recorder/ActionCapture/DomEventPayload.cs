using System.Text.Json.Serialization;

namespace Motus.Recorder.ActionCapture;

/// <summary>
/// Flat record representing a DOM event sent from the injected JS listener.
/// Different event types populate different subsets of fields.
/// </summary>
internal sealed record DomEventPayload
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public double Timestamp { get; init; }

    [JsonPropertyName("x")]
    public double? X { get; init; }

    [JsonPropertyName("y")]
    public double? Y { get; init; }

    [JsonPropertyName("button")]
    public string? Button { get; init; }

    [JsonPropertyName("clickCount")]
    public int? ClickCount { get; init; }

    [JsonPropertyName("modifiers")]
    public int? Modifiers { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }

    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("checked")]
    public bool? Checked { get; init; }

    [JsonPropertyName("selectedValues")]
    public string[]? SelectedValues { get; init; }

    [JsonPropertyName("tagName")]
    public string? TagName { get; init; }

    [JsonPropertyName("inputType")]
    public string? InputType { get; init; }

    [JsonPropertyName("scrollX")]
    public double? ScrollX { get; init; }

    [JsonPropertyName("scrollY")]
    public double? ScrollY { get; init; }

    [JsonPropertyName("pageUrl")]
    public string? PageUrl { get; init; }
}
