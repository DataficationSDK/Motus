using System.Collections.Immutable;

namespace Motus.Codegen.Model;

/// <summary>
/// A CDP command (method) within a domain.
/// </summary>
internal readonly record struct CdpCommand(
    string Name,
    ImmutableArray<CdpProperty> Parameters,
    ImmutableArray<CdpProperty> Returns,
    bool Deprecated,
    bool Experimental);
