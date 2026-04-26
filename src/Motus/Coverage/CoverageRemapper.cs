using System;
using System.Collections.Generic;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Remaps coverage ranges from a generated asset back to its original source files
/// using a Source Map v3 document. Granularity is line-level: each generated covered
/// region contributes covered lines to one or more original sources.
/// </summary>
internal static class CoverageRemapper
{
    /// <summary>
    /// Project the covered ranges of a generated asset onto its original source files.
    /// </summary>
    /// <param name="generatedSource">Full text of the generated asset (used to convert byte offsets to (line,column)).</param>
    /// <param name="generatedRanges">Coverage ranges in offsets within <paramref name="generatedSource"/>.</param>
    /// <param name="map">Parsed source map for the generated asset.</param>
    /// <returns>One <see cref="OriginalFileCoverage"/> per referenced source.</returns>
    public static IReadOnlyList<OriginalFileCoverage> Remap(
        string generatedSource,
        IReadOnlyList<CoverageRange> generatedRanges,
        SourceMap map)
    {
        if (string.IsNullOrEmpty(generatedSource) || generatedRanges.Count == 0 || map.Lines.Count == 0)
            return Array.Empty<OriginalFileCoverage>();

        var genLineStarts = ComputeLineStarts(generatedSource);

        // Bucket: (sourceIdx, originalLine0Based) -> aggregated count.
        var hits = new Dictionary<(int Source, int Line), int>();

        foreach (var range in generatedRanges)
        {
            if (range.EndOffset <= range.StartOffset)
                continue;

            int startLine = LineOfOffset(genLineStarts, range.StartOffset);
            int endLine = LineOfOffset(genLineStarts, Math.Max(range.StartOffset, range.EndOffset - 1));

            for (int line = startLine; line <= endLine && line < map.Lines.Count; line++)
            {
                int lineStart = genLineStarts[line];
                int lineEnd = (line + 1 < genLineStarts.Count) ? genLineStarts[line + 1] : generatedSource.Length;

                int colStart = (line == startLine) ? range.StartOffset - lineStart : 0;
                int colEnd = (line == endLine) ? range.EndOffset - lineStart : lineEnd - lineStart;

                var segments = map.Lines[line].Segments;
                for (int i = 0; i < segments.Count; i++)
                {
                    var seg = segments[i];
                    if (seg.SourceIndex is null || seg.OriginalLine is null)
                        continue;

                    int segColStart = seg.GeneratedColumn;
                    int segColEnd = (i + 1 < segments.Count)
                        ? segments[i + 1].GeneratedColumn
                        : lineEnd - lineStart;

                    if (segColEnd <= colStart || segColStart >= colEnd)
                        continue;

                    var key = (seg.SourceIndex.Value, seg.OriginalLine.Value);
                    hits.TryGetValue(key, out var prev);
                    hits[key] = prev + range.Count;
                }
            }
        }

        if (hits.Count == 0)
            return Array.Empty<OriginalFileCoverage>();

        // Group by source file.
        var bySource = new Dictionary<int, List<(int Line, int Count)>>();
        foreach (var ((sourceIdx, line), count) in hits)
        {
            if (!bySource.TryGetValue(sourceIdx, out var list))
            {
                list = new List<(int, int)>();
                bySource[sourceIdx] = list;
            }
            list.Add((line, count));
        }

        var result = new List<OriginalFileCoverage>(bySource.Count);
        foreach (var (sourceIdx, lineHits) in bySource)
        {
            if (sourceIdx < 0 || sourceIdx >= map.Sources.Count)
                continue;

            var path = ResolveSourcePath(map, sourceIdx);
            string? content = (sourceIdx < map.SourcesContent.Count) ? map.SourcesContent[sourceIdx] : null;

            lineHits.Sort((a, b) => a.Line.CompareTo(b.Line));

            IReadOnlyList<CoverageRange> ranges;
            FileCoverageStats stats;

            if (!string.IsNullOrEmpty(content))
            {
                var origLineStarts = ComputeLineStarts(content);
                var rangeList = new List<CoverageRange>(lineHits.Count);
                foreach (var (line, count) in lineHits)
                {
                    if (line < 0 || line >= origLineStarts.Count)
                        continue;
                    int lineStart = origLineStarts[line];
                    int lineEnd = (line + 1 < origLineStarts.Count) ? origLineStarts[line + 1] : content!.Length;
                    if (lineEnd <= lineStart)
                        continue;
                    rangeList.Add(new CoverageRange(lineStart, lineEnd, count));
                }

                ranges = CoverageAggregator.MergeRanges(rangeList);
                stats = CoverageAggregator.SummarizeScript(content!, ranges);
            }
            else
            {
                // No source content; emit synthetic line-marker ranges so reporters can
                // still enumerate covered lines. Stats only describe what we observed.
                var rangeList = new List<CoverageRange>(lineHits.Count);
                int coveredLines = 0;
                foreach (var (line, count) in lineHits)
                {
                    if (line < 0) continue;
                    rangeList.Add(new CoverageRange(line, line + 1, count));
                    if (count > 0) coveredLines++;
                }
                ranges = rangeList;
                stats = new FileCoverageStats(coveredLines, coveredLines, coveredLines > 0 ? 100 : 0);
            }

            result.Add(new OriginalFileCoverage(path, content, ranges, stats));
        }

        return result;
    }

    private static string ResolveSourcePath(SourceMap map, int sourceIdx)
    {
        var src = map.Sources[sourceIdx];
        if (string.IsNullOrEmpty(map.SourceRoot))
            return src;
        if (string.IsNullOrEmpty(src))
            return map.SourceRoot;
        if (map.SourceRoot.EndsWith('/') || src.StartsWith('/'))
            return map.SourceRoot + src;
        return map.SourceRoot + "/" + src;
    }

    private static List<int> ComputeLineStarts(string text)
    {
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                starts.Add(i + 1);
        }
        return starts;
    }

    private static int LineOfOffset(List<int> lineStarts, int offset)
    {
        // Binary search: largest index i where lineStarts[i] <= offset.
        int lo = 0, hi = lineStarts.Count - 1;
        while (lo < hi)
        {
            int mid = (lo + hi + 1) >> 1;
            if (lineStarts[mid] <= offset) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }
}
