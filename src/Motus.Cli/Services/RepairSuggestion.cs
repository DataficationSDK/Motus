using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Motus.Cli.Services;

/// <summary>
/// Confidence ladder for a repair suggestion, derived from how the moved
/// element was matched against the stored manifest fingerprint.
/// </summary>
#pragma warning disable SYSLIB1034
[JsonConverter(typeof(JsonStringEnumConverter))]
#pragma warning restore SYSLIB1034
internal enum Confidence
{
    High,
    Medium,
    Low,
}

/// <summary>
/// How <see cref="FingerprintScanner"/> matched a candidate element to the
/// stored fingerprint. Maps directly to <see cref="Confidence"/>.
/// </summary>
internal enum FingerprintMatchQuality
{
    /// <summary>SHA-256 recompute matches the manifest hash exactly.</summary>
    Hash,
    /// <summary>All manifest key attributes match, or at least 3 match.</summary>
    Attributes,
    /// <summary>Same tag and ancestor path; fewer than 3 attributes match.</summary>
    Ancestor,
}

/// <summary>
/// A candidate element resolved against the manifest fingerprint along with
/// the quality of the match.
/// </summary>
internal sealed record FingerprintMatch(
    FingerprintCandidate Candidate,
    FingerprintMatchQuality Quality);

/// <summary>
/// One entry in the ranked list of replacement suggestions for a broken selector.
/// </summary>
internal sealed record RepairSuggestion(
    string Replacement,
    string StrategyName,
    Confidence Confidence);

[ExcludeFromCodeCoverage]
internal static class ConfidenceMapping
{
    internal static Confidence FromQuality(FingerprintMatchQuality quality) => quality switch
    {
        FingerprintMatchQuality.Hash => Confidence.High,
        FingerprintMatchQuality.Attributes => Confidence.Medium,
        FingerprintMatchQuality.Ancestor => Confidence.Low,
        _ => Confidence.Low,
    };
}
