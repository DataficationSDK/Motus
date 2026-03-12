using System.Collections.Immutable;

namespace Motus.Codegen.Model;

/// <summary>
/// A single property/parameter in a CDP type, command, or event.
/// </summary>
internal readonly record struct CdpProperty(
    string Name,
    string? TypeRef,
    string? TypeName,
    bool Optional,
    string? ArrayItemRef,
    string? ArrayItemType,
    bool Deprecated,
    ImmutableArray<string> InlineEnum);
