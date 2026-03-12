using System.Collections.Immutable;

namespace Motus.Codegen.Model;

/// <summary>
/// A CDP type definition within a domain.
/// </summary>
internal readonly record struct CdpType(
    string Id,
    string? UnderlyingType,
    CdpTypeKind Kind,
    ImmutableArray<string> EnumValues,
    ImmutableArray<CdpProperty> Properties,
    string? ArrayItemRef,
    string? ArrayItemType,
    bool Deprecated,
    bool Experimental);
