using System.Collections.Immutable;

namespace Motus.Codegen.Model;

/// <summary>
/// A CDP event within a domain.
/// </summary>
internal readonly record struct CdpEvent(
    string Name,
    ImmutableArray<CdpProperty> Parameters,
    bool Deprecated,
    bool Experimental);
