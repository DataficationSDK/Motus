using System.Collections.Generic;

namespace Motus;

/// <summary>
/// A single mapping segment within a source-map line. Field semantics match
/// Source Map v3: a segment may carry 1 field (generated column only) or 4-5
/// fields (generated column, source index, original line, original column,
/// optional name index).
/// </summary>
internal sealed record MappingSegment(
    int GeneratedColumn,
    int? SourceIndex,
    int? OriginalLine,
    int? OriginalColumn,
    int? NameIndex);

/// <summary>
/// All segments for a single line of generated source.
/// </summary>
internal sealed record MappingLine(IReadOnlyList<MappingSegment> Segments);

/// <summary>
/// Parsed Source Map v3 with absolute (decoded) segment fields.
/// </summary>
internal sealed record SourceMap(
    int Version,
    IReadOnlyList<string> Sources,
    IReadOnlyList<string?> SourcesContent,
    IReadOnlyList<MappingLine> Lines,
    string? SourceRoot);

/// <summary>
/// Wire-format DTO for the source-map JSON document. Only the fields required for
/// coverage remapping are deserialized.
/// </summary>
internal sealed record SourceMapJsonDto(
    int Version,
    string Mappings,
    string[] Sources,
    string?[]? SourcesContent = null,
    string? SourceRoot = null,
    string? File = null,
    string[]? Names = null);
