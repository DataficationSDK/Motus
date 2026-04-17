namespace Motus.Cli.Services;

/// <summary>
/// Renders <see cref="SelectorCheckResult"/>s as a fixed-width ANSI-colored table
/// matching the style of <see cref="Reporters.ConsoleReporter"/>.
/// </summary>
internal static class SelectorCheckTablePrinter
{
    private const string Green  = "\x1b[32m";
    private const string Red    = "\x1b[31m";
    private const string Yellow = "\x1b[33m";
    private const string Gray   = "\x1b[90m";
    private const string Reset  = "\x1b[0m";

    private const int StatusWidth   = 10;
    private const int SelectorWidth = 40;
    private const int LocationWidth = 35;
    private const int MatchesWidth  = 7;

    internal static void Print(
        IReadOnlyList<SelectorCheckResult> results,
        TextWriter writer,
        bool useColor,
        RewriteReport? rewriteReport = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(writer);

        WriteHeader(writer);
        foreach (var r in results)
        {
            WriteRow(writer, r, useColor);
        }
        WriteSummary(writer, results, useColor);
        WriteRewriteSummary(writer, results, rewriteReport, useColor);
    }

    private static void WriteHeader(TextWriter writer)
    {
        writer.WriteLine(
            $"{"STATUS",-StatusWidth} {"SELECTOR",-SelectorWidth} {"FILE:LINE",-LocationWidth} {"MATCHES",MatchesWidth}");
        writer.WriteLine(
            $"{new string('-', StatusWidth)} {new string('-', SelectorWidth)} {new string('-', LocationWidth)} {new string('-', MatchesWidth)}");
    }

    private static void WriteRow(TextWriter writer, SelectorCheckResult r, bool useColor)
    {
        var statusLabel = r.Fixed ? "FIXED" : r.Status.ToString().ToUpperInvariant();
        var selector = Format(r.LocatorMethod, r.Selector);
        var location = $"{Path.GetFileName(r.SourceFile)}:{r.SourceLine}";

        var selectorCol = Truncate(selector, SelectorWidth);
        var locationCol = Truncate(location, LocationWidth);
        var matchesCol = r.MatchCount.ToString();

        if (useColor)
        {
            var color = r.Fixed ? Green : ColorFor(r.Status);
            writer.WriteLine(
                $"{color}{statusLabel,-StatusWidth}{Reset} {selectorCol,-SelectorWidth} {locationCol,-LocationWidth} {matchesCol,MatchesWidth}");
        }
        else
        {
            writer.WriteLine(
                $"{statusLabel,-StatusWidth} {selectorCol,-SelectorWidth} {locationCol,-LocationWidth} {matchesCol,MatchesWidth}");
        }

        // Preserve the pre-3D single-line format when only the legacy Suggestion is set.
        if (r.Suggestions is { Count: > 0 } suggestions)
        {
            foreach (var s in suggestions)
            {
                var line = $"  -> Suggestion ({s.Confidence}, {s.StrategyName}): {s.Replacement}";
                writer.WriteLine(useColor ? $"{Gray}{line}{Reset}" : line);
            }
        }
        else if (r.Suggestion is not null)
        {
            var line = $"  -> Suggestion: {r.Suggestion}";
            writer.WriteLine(useColor ? $"{Gray}{line}{Reset}" : line);
        }

        if (r.Fixed && r.AppliedSuggestion is not null)
        {
            var line = $"  -> Fixed: {r.AppliedSuggestion}";
            writer.WriteLine(useColor ? $"{Green}{line}{Reset}" : line);
        }

        if (r.FixError is not null)
        {
            var line = $"  -> Fix error: {r.FixError}";
            writer.WriteLine(useColor ? $"{Yellow}{line}{Reset}" : line);
        }

        if (r.Note is not null)
        {
            var line = $"  -> Note: {r.Note}";
            writer.WriteLine(useColor ? $"{Gray}{line}{Reset}" : line);
        }
    }

    private static void WriteSummary(
        TextWriter writer, IReadOnlyList<SelectorCheckResult> results, bool useColor)
    {
        int healthy = 0, broken = 0, ambiguous = 0, skipped = 0;
        foreach (var r in results)
        {
            switch (r.Status)
            {
                case SelectorCheckStatus.Healthy:   healthy++; break;
                case SelectorCheckStatus.Broken:    broken++; break;
                case SelectorCheckStatus.Ambiguous: ambiguous++; break;
                case SelectorCheckStatus.Skipped:   skipped++; break;
            }
        }

        writer.WriteLine();
        if (useColor)
        {
            writer.WriteLine(
                $"Total {results.Count}  |  {Green}{healthy} healthy{Reset}  |  {Red}{broken} broken{Reset}  |  {Yellow}{ambiguous} ambiguous{Reset}  |  {Gray}{skipped} skipped{Reset}");
        }
        else
        {
            writer.WriteLine(
                $"Total {results.Count}  |  {healthy} healthy  |  {broken} broken  |  {ambiguous} ambiguous  |  {skipped} skipped");
        }
    }

    private static void WriteRewriteSummary(
        TextWriter writer,
        IReadOnlyList<SelectorCheckResult> results,
        RewriteReport? report,
        bool useColor)
    {
        if (report is null)
            return;

        var brokenTotal = 0;
        foreach (var r in results)
        {
            if (r.Status == SelectorCheckStatus.Broken)
                brokenTotal++;
        }

        var line = $"Fixed {report.FixesApplied} of {brokenTotal} broken selectors in {report.FilesModified} files";
        writer.WriteLine(useColor ? $"{Green}{line}{Reset}" : line);
    }

    private static string ColorFor(SelectorCheckStatus status) => status switch
    {
        SelectorCheckStatus.Healthy   => Green,
        SelectorCheckStatus.Broken    => Red,
        SelectorCheckStatus.Ambiguous => Yellow,
        SelectorCheckStatus.Skipped   => Gray,
        _                             => Reset,
    };

    private static string Format(string locatorMethod, string selector) =>
        locatorMethod == "Locator"
            ? selector
            : $"{locatorMethod}({selector})";

    private static string Truncate(string value, int width)
    {
        if (value.Length <= width)
            return value;
        return value[..(width - 1)] + "\u2026";
    }
}
