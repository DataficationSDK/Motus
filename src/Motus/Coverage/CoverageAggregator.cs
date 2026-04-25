using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Merges coverage ranges across multiple page sessions and computes line-level
/// (script) and rule-level (stylesheet) summary statistics.
/// </summary>
internal static class CoverageAggregator
{
    /// <summary>
    /// Merges overlapping/adjacent ranges into a sorted, non-overlapping list using a
    /// sweep over distinct offsets. Counts of overlapping inputs are summed.
    /// </summary>
    internal static IReadOnlyList<CoverageRange> MergeRanges(IEnumerable<CoverageRange> ranges)
    {
        var input = ranges as IList<CoverageRange> ?? ranges.ToList();
        if (input.Count == 0)
            return Array.Empty<CoverageRange>();

        var offsets = new SortedSet<int>();
        foreach (var r in input)
        {
            if (r.EndOffset <= r.StartOffset)
                continue;
            offsets.Add(r.StartOffset);
            offsets.Add(r.EndOffset);
        }

        if (offsets.Count < 2)
            return Array.Empty<CoverageRange>();

        var sorted = offsets.ToArray();
        var result = new List<CoverageRange>();
        for (int i = 0; i < sorted.Length - 1; i++)
        {
            int s = sorted[i], e = sorted[i + 1];
            if (s >= e)
                continue;

            int total = 0;
            bool covered = false;
            foreach (var r in input)
            {
                if (r.StartOffset <= s && r.EndOffset >= e)
                {
                    covered = true;
                    total += r.Count;
                }
            }

            if (!covered)
                continue;

            if (result.Count > 0 && result[^1].EndOffset == s && result[^1].Count == total)
                result[^1] = new CoverageRange(result[^1].StartOffset, e, total);
            else
                result.Add(new CoverageRange(s, e, total));
        }

        return result;
    }

    /// <summary>
    /// Computes line-level coverage by splitting the source on '\n' and marking each line
    /// as covered if any range with count &gt; 0 intersects it.
    /// </summary>
    internal static FileCoverageStats SummarizeScript(string source, IReadOnlyList<CoverageRange> ranges)
    {
        if (string.IsNullOrEmpty(source))
            return new FileCoverageStats(0, 0, 0);

        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
                lineStarts.Add(i + 1);
        }

        int totalLines = lineStarts.Count;
        if (lineStarts[^1] >= source.Length)
            totalLines--;

        if (totalLines <= 0)
            return new FileCoverageStats(0, 0, 0);

        var covered = new List<CoverageRange>();
        foreach (var r in ranges)
        {
            if (r.Count > 0 && r.EndOffset > r.StartOffset)
                covered.Add(r);
        }

        int coveredLines = 0;
        for (int i = 0; i < totalLines; i++)
        {
            int lineStart = lineStarts[i];
            int lineEnd = i + 1 < lineStarts.Count ? lineStarts[i + 1] - 1 : source.Length;
            if (lineEnd <= lineStart)
                continue;

            foreach (var r in covered)
            {
                if (r.StartOffset < lineEnd && r.EndOffset > lineStart)
                {
                    coveredLines++;
                    break;
                }
            }
        }

        double pct = totalLines > 0 ? coveredLines * 100.0 / totalLines : 0;
        return new FileCoverageStats(totalLines, coveredLines, pct);
    }

    /// <summary>
    /// Computes rule-level coverage from a list of CSS rule usage entries.
    /// Each rule is one unit; the rule is "covered" if <see cref="CssRuleUsage.Used"/> is true.
    /// </summary>
    internal static FileCoverageStats SummarizeStylesheet(IReadOnlyList<CssRuleUsage> rules)
    {
        int total = rules.Count;
        if (total == 0)
            return new FileCoverageStats(0, 0, 0);

        int used = 0;
        foreach (var r in rules)
        {
            if (r.Used) used++;
        }

        double pct = used * 100.0 / total;
        return new FileCoverageStats(total, used, pct);
    }

    /// <summary>
    /// Merges multiple <see cref="ScriptCoverage"/> snapshots by URL. Ranges from the same
    /// URL are unioned via <see cref="MergeRanges"/>; per-script line stats are recomputed.
    /// Source text is taken from the first snapshot for each URL.
    /// </summary>
    internal static IReadOnlyList<ScriptCoverage> MergeScripts(IEnumerable<ScriptCoverage> snapshots)
    {
        var byUrl = new Dictionary<string, (string Source, List<CoverageRange> Ranges)>(StringComparer.Ordinal);
        foreach (var s in snapshots)
        {
            if (!byUrl.TryGetValue(s.Url, out var entry))
            {
                entry = (s.Source, new List<CoverageRange>());
                byUrl[s.Url] = entry;
            }
            entry.Ranges.AddRange(s.Ranges);
        }

        var result = new List<ScriptCoverage>(byUrl.Count);
        foreach (var (url, entry) in byUrl)
        {
            var merged = MergeRanges(entry.Ranges);
            var stats = SummarizeScript(entry.Source, merged);
            result.Add(new ScriptCoverage(url, entry.Source, merged, stats));
        }
        return result;
    }

    /// <summary>
    /// Merges multiple <see cref="StylesheetCoverage"/> snapshots by URL. A rule is reported
    /// as used if it was used in any snapshot.
    /// </summary>
    internal static IReadOnlyList<StylesheetCoverage> MergeStylesheets(IEnumerable<StylesheetCoverage> snapshots)
    {
        var byUrl = new Dictionary<string, (string Source, Dictionary<(int Start, int End), bool> Rules)>(StringComparer.Ordinal);
        foreach (var s in snapshots)
        {
            if (!byUrl.TryGetValue(s.Url, out var entry))
            {
                entry = (s.Source, new Dictionary<(int, int), bool>());
                byUrl[s.Url] = entry;
            }
            foreach (var rule in s.Rules)
            {
                var key = (rule.StartOffset, rule.EndOffset);
                entry.Rules[key] = entry.Rules.TryGetValue(key, out var prev) ? prev || rule.Used : rule.Used;
            }
        }

        var result = new List<StylesheetCoverage>(byUrl.Count);
        foreach (var (url, entry) in byUrl)
        {
            var rules = entry.Rules
                .OrderBy(kv => kv.Key.Start)
                .ThenBy(kv => kv.Key.End)
                .Select(kv => new CssRuleUsage(kv.Key.Start, kv.Key.End, kv.Value))
                .ToList();
            var stats = SummarizeStylesheet(rules);
            result.Add(new StylesheetCoverage(url, entry.Source, rules, stats));
        }
        return result;
    }

    /// <summary>
    /// Builds the cross-file summary across all scripts and stylesheets.
    /// </summary>
    internal static CoverageSummary BuildSummary(
        IReadOnlyList<ScriptCoverage> scripts,
        IReadOnlyList<StylesheetCoverage> stylesheets)
    {
        int totalLines = 0, coveredLines = 0;
        foreach (var s in scripts)
        {
            totalLines += s.Stats.TotalLines;
            coveredLines += s.Stats.CoveredLines;
        }

        int totalRules = 0, usedRules = 0;
        foreach (var s in stylesheets)
        {
            totalRules += s.Stats.TotalLines;
            usedRules += s.Stats.CoveredLines;
        }

        double linePct = totalLines > 0 ? coveredLines * 100.0 / totalLines : 0;
        double rulePct = totalRules > 0 ? usedRules * 100.0 / totalRules : 0;

        return new CoverageSummary(totalLines, coveredLines, linePct, totalRules, usedRules, rulePct);
    }
}
