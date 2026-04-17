using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Motus.Cli.Services;

// On .NET 8, Motus ships an internal JsonStringEnumConverter<T> polyfill that is
// visible to this assembly via InternalsVisibleTo and collides with the generic
// BCL type introduced in .NET 9. Fall back to the non-generic BCL converter and
// accept the AOT warning — Motus.Cli is not AOT-published.
#pragma warning disable SYSLIB1034
[JsonConverter(typeof(JsonStringEnumConverter))]
#pragma warning restore SYSLIB1034
internal enum SelectorCheckStatus
{
    Healthy,
    Broken,
    Ambiguous,
    Skipped,
}

/// <summary>
/// Outcome of validating a single parsed selector against a live page.
/// </summary>
internal sealed record SelectorCheckResult(
    SelectorCheckStatus Status,
    string Selector,
    string LocatorMethod,
    string SourceFile,
    int SourceLine,
    string PageUrl,
    int MatchCount,
    string? Suggestion,
    string? Note);

[JsonSerializable(typeof(List<SelectorCheckResult>))]
[JsonSerializable(typeof(SelectorCheckResult))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[ExcludeFromCodeCoverage]
internal sealed partial class CheckResultsJsonContext : JsonSerializerContext;
