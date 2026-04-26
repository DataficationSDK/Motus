using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Motus;

/// <summary>
/// Parses Source Map v3 JSON into a typed <see cref="SourceMap"/>, decoding the
/// VLQ-packed <c>mappings</c> field into absolute segment values.
/// </summary>
internal static class SourceMapParser
{
    /// <summary>
    /// Parse a source-map JSON string. Throws on malformed input or unsupported version.
    /// </summary>
    public static SourceMap Parse(string json)
    {
        var dto = JsonSerializer.Deserialize(json, CdpJsonContext.Default.SourceMapJsonDto)
            ?? throw new FormatException("Source map JSON is null.");

        if (dto.Version != 3)
            throw new FormatException($"Unsupported source map version: {dto.Version} (expected 3).");

        var sources = dto.Sources ?? Array.Empty<string>();
        var sourcesContent = dto.SourcesContent ?? Array.Empty<string?>();
        var lines = DecodeMappings(dto.Mappings ?? string.Empty);

        return new SourceMap(
            Version: dto.Version,
            Sources: sources,
            SourcesContent: sourcesContent,
            Lines: lines,
            SourceRoot: dto.SourceRoot);
    }

    /// <summary>
    /// Walk the mappings string segment-by-segment, applying running deltas per
    /// the v3 spec. Lines are separated by ';'; segments within a line by ','.
    /// </summary>
    private static IReadOnlyList<MappingLine> DecodeMappings(string mappings)
    {
        var lines = new List<MappingLine>();

        int generatedColumn = 0;
        int sourceIndex = 0;
        int originalLine = 0;
        int originalColumn = 0;
        int nameIndex = 0;

        int i = 0;
        while (i <= mappings.Length)
        {
            var segments = new List<MappingSegment>();
            generatedColumn = 0;

            while (i < mappings.Length && mappings[i] != ';')
            {
                int segStart = i;
                while (i < mappings.Length && mappings[i] != ',' && mappings[i] != ';')
                    i++;

                int segLen = i - segStart;
                if (segLen > 0)
                {
                    var segText = mappings.Substring(segStart, segLen);
                    var fields = Vlq.Decode(segText);

                    if (fields.Count != 1 && fields.Count != 4 && fields.Count != 5)
                        throw new FormatException($"Invalid source-map segment '{segText}' (got {fields.Count} fields).");

                    generatedColumn += fields[0];

                    int? srcIdx = null, origLine = null, origCol = null, nmIdx = null;
                    if (fields.Count >= 4)
                    {
                        sourceIndex += fields[1];
                        originalLine += fields[2];
                        originalColumn += fields[3];
                        srcIdx = sourceIndex;
                        origLine = originalLine;
                        origCol = originalColumn;
                    }
                    if (fields.Count == 5)
                    {
                        nameIndex += fields[4];
                        nmIdx = nameIndex;
                    }

                    segments.Add(new MappingSegment(generatedColumn, srcIdx, origLine, origCol, nmIdx));
                }

                if (i < mappings.Length && mappings[i] == ',')
                    i++;
            }

            lines.Add(new MappingLine(segments));

            if (i >= mappings.Length)
                break;
            i++; // skip ';'
        }

        return lines;
    }
}
