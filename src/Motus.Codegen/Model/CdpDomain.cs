using System.Collections.Immutable;

namespace Motus.Codegen.Model;

/// <summary>
/// A parsed CDP domain containing types, commands, and events.
/// </summary>
internal readonly record struct CdpDomain(
    string Name,
    ImmutableArray<CdpType> Types,
    ImmutableArray<CdpCommand> Commands,
    ImmutableArray<CdpEvent> Events,
    bool Deprecated,
    bool Experimental);
