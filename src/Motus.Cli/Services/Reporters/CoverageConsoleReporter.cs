using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

/// <summary>
/// Prints a per-file coverage summary table after the run, color-coded by percentage:
/// green &gt; 80%, yellow 50-80%, red &lt; 50%.
/// </summary>
public sealed class CoverageConsoleReporter(TextWriter writer, bool useColor) : ICoverageReporter
{
    private const string Green = "\x1b[32m";
    private const string Yellow = "\x1b[33m";
    private const string Red = "\x1b[31m";
    private const string Gray = "\x1b[90m";
    private const string Reset = "\x1b[0m";

    public CoverageConsoleReporter() : this(Console.Out, !Console.IsOutputRedirected) { }

    public Task OnCoverageCollectedAsync(CoverageData coverage, TestInfo test) => Task.CompletedTask;

    public Task OnCoverageRunEndAsync(CoverageData aggregated)
    {
        writer.WriteLine();
        writer.WriteLine("Coverage Summary");

        var rows = new List<(string File, int Lines, int Covered, double Pct)>();
        foreach (var s in aggregated.Scripts)
            rows.Add((Shorten(s.Url), s.Stats.TotalLines, s.Stats.CoveredLines, s.Stats.Percentage));
        foreach (var s in aggregated.Stylesheets)
            rows.Add((Shorten(s.Url), s.Stats.TotalLines, s.Stats.CoveredLines, s.Stats.Percentage));

        if (rows.Count == 0)
        {
            writer.WriteLine("  (no coverage data collected)");
            if (aggregated.DiagnosticMessage is not null)
                writer.WriteLine($"  {aggregated.DiagnosticMessage}");
            return Task.CompletedTask;
        }

        var nameWidth = Math.Max(4, Math.Min(60, rows.Max(r => r.File.Length)));
        var header = $"  {"File".PadRight(nameWidth)}  {"Lines",6}  {"Covered",7}  {"%",6}";
        writer.WriteLine(header);
        writer.WriteLine("  " + new string('-', nameWidth + 26));

        foreach (var row in rows.OrderBy(r => r.File, StringComparer.Ordinal))
        {
            var pctStr = $"{row.Pct,5:F1}%";
            var line = $"  {row.File.PadRight(nameWidth)}  {row.Lines,6}  {row.Covered,7}  {(useColor ? Color(row.Pct) + pctStr + Reset : pctStr)}";
            writer.WriteLine(line);
        }

        writer.WriteLine("  " + new string('-', nameWidth + 26));

        var summary = aggregated.Summary;
        var jsLine = $"  Overall JS:  {summary.CoveredLines}/{summary.TotalLines} lines";
        var cssLine = $"  Overall CSS: {summary.UsedCssRules}/{summary.TotalCssRules} rules";
        if (useColor)
        {
            writer.WriteLine($"{jsLine,-40}  {Color(summary.LinePercentage)}{summary.LinePercentage,5:F1}%{Reset}");
            writer.WriteLine($"{cssLine,-40}  {Color(summary.CssPercentage)}{summary.CssPercentage,5:F1}%{Reset}");
        }
        else
        {
            writer.WriteLine($"{jsLine,-40}  {summary.LinePercentage,5:F1}%");
            writer.WriteLine($"{cssLine,-40}  {summary.CssPercentage,5:F1}%");
        }

        if (aggregated.DiagnosticMessage is not null)
            writer.WriteLine($"  {Gray}{aggregated.DiagnosticMessage}{Reset}");

        return Task.CompletedTask;
    }

    private static string Color(double pct) => pct switch
    {
        > 80.0 => Green,
        >= 50.0 => Yellow,
        _ => Red,
    };

    private static string Shorten(string url)
    {
        if (string.IsNullOrEmpty(url)) return "(unknown)";
        if (url.Length <= 60) return url;
        return "..." + url[^57..];
    }
}
