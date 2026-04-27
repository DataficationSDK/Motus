using System.Security.Cryptography;
using System.Text;
using System.Web;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

/// <summary>
/// Writes a static HTML coverage site to a directory: an index page with a file tree
/// and per-file source views with line-by-line coverage highlighting.
/// </summary>
public sealed class CoverageHtmlReporter(string outputDirectory) : ICoverageReporter
{
    public Task OnCoverageCollectedAsync(CoverageData coverage, TestInfo test) => Task.CompletedTask;

    public async Task OnCoverageRunEndAsync(CoverageData aggregated)
    {
        Directory.CreateDirectory(outputDirectory);

        var fileEntries = new List<FileEntry>();

        foreach (var s in aggregated.Scripts)
        {
            var fileName = MakeFileName(s.Url, "js");
            await WriteScriptPageAsync(s, fileName);
            fileEntries.Add(new FileEntry("JS", s.Url, fileName, s.Stats));
        }

        foreach (var s in aggregated.Stylesheets)
        {
            var fileName = MakeFileName(s.Url, "css");
            await WriteStylesheetPageAsync(s, fileName);
            fileEntries.Add(new FileEntry("CSS", s.Url, fileName, s.Stats));
        }

        await WriteIndexAsync(aggregated, fileEntries);
    }

    private async Task WriteIndexAsync(CoverageData data, List<FileEntry> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"><title>Motus Coverage Report</title>");
        sb.AppendLine("<style>");
        sb.Append(SharedCss);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<h1>Coverage Report</h1>");

        var s = data.Summary;
        sb.AppendLine("<div class=\"gauges\">");
        sb.AppendLine(GaugeHtml("JavaScript Lines", s.CoveredLines, s.TotalLines, s.LinePercentage));
        sb.AppendLine(GaugeHtml("CSS Rules", s.UsedCssRules, s.TotalCssRules, s.CssPercentage));
        sb.AppendLine("</div>");

        if (data.DiagnosticMessage is not null)
            sb.AppendLine($"<p class=\"diagnostic\">{HttpUtility.HtmlEncode(data.DiagnosticMessage)}</p>");

        sb.AppendLine("<table class=\"file-tree\"><thead><tr><th>Type</th><th>File</th><th>Lines</th><th>Covered</th><th>%</th><th></th></tr></thead><tbody>");
        foreach (var f in files.OrderBy(x => x.Url, StringComparer.Ordinal))
        {
            var pctClass = PercentClass(f.Stats.Percentage);
            sb.Append("<tr>");
            sb.Append($"<td>{f.Kind}</td>");
            sb.Append($"<td><a href=\"{HttpUtility.HtmlAttributeEncode(f.RelativePath)}\">{HttpUtility.HtmlEncode(f.Url)}</a></td>");
            sb.Append($"<td>{f.Stats.TotalLines}</td>");
            sb.Append($"<td>{f.Stats.CoveredLines}</td>");
            sb.Append($"<td class=\"{pctClass}\">{f.Stats.Percentage:F1}%</td>");
            sb.Append($"<td class=\"bar\"><span class=\"{pctClass}\" style=\"width:{Math.Clamp(f.Stats.Percentage, 0, 100):F1}%\"></span></td>");
            sb.AppendLine("</tr>");
        }
        sb.AppendLine("</tbody></table>");

        sb.AppendLine($"<footer>Generated {data.CollectedAtUtc:u}</footer>");
        sb.AppendLine("</body></html>");

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "index.html"), sb.ToString());
    }

    private async Task WriteScriptPageAsync(ScriptCoverage script, string fileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html><head><meta charset=\"utf-8\"><title>{HttpUtility.HtmlEncode(script.Url)}</title>");
        sb.AppendLine("<style>");
        sb.Append(SharedCss);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<p><a href=\"index.html\">&larr; Back to index</a></p>");
        sb.AppendLine($"<h1>{HttpUtility.HtmlEncode(script.Url)}</h1>");

        var pctClass = PercentClass(script.Stats.Percentage);
        sb.AppendLine($"<p>Lines: {script.Stats.CoveredLines}/{script.Stats.TotalLines} <span class=\"{pctClass}\">({script.Stats.Percentage:F1}%)</span></p>");

        AppendSourceWithLineCoverage(sb, script.Source, script.Ranges);

        sb.AppendLine("</body></html>");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString());
    }

    private async Task WriteStylesheetPageAsync(StylesheetCoverage sheet, string fileName)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html><head><meta charset=\"utf-8\"><title>{HttpUtility.HtmlEncode(sheet.Url)}</title>");
        sb.AppendLine("<style>");
        sb.Append(SharedCss);
        sb.AppendLine("</style></head><body>");
        sb.AppendLine("<p><a href=\"index.html\">&larr; Back to index</a></p>");
        sb.AppendLine($"<h1>{HttpUtility.HtmlEncode(sheet.Url)}</h1>");

        var pctClass = PercentClass(sheet.Stats.Percentage);
        sb.AppendLine($"<p>Rules: {sheet.Stats.CoveredLines}/{sheet.Stats.TotalLines} <span class=\"{pctClass}\">({sheet.Stats.Percentage:F1}%)</span></p>");

        var ranges = sheet.Rules
            .Where(r => r.EndOffset > r.StartOffset)
            .Select(r => new CoverageRange(r.StartOffset, r.EndOffset, r.Used ? 1 : 0))
            .ToList();

        AppendSourceWithLineCoverage(sb, sheet.Source, ranges);

        sb.AppendLine("</body></html>");
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, fileName), sb.ToString());
    }

    private static void AppendSourceWithLineCoverage(StringBuilder sb, string source, IReadOnlyList<CoverageRange> ranges)
    {
        if (string.IsNullOrEmpty(source))
        {
            sb.AppendLine("<p><em>(source not available)</em></p>");
            return;
        }

        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
            if (source[i] == '\n') lineStarts.Add(i + 1);

        sb.AppendLine("<table class=\"source\"><tbody>");
        for (int i = 0; i < lineStarts.Count; i++)
        {
            var lineStart = lineStarts[i];
            var lineEnd = i + 1 < lineStarts.Count ? lineStarts[i + 1] - 1 : source.Length;
            if (lineStart >= source.Length && i + 1 >= lineStarts.Count) break;

            var lineText = source.Substring(lineStart, Math.Max(0, lineEnd - lineStart));
            var trimmed = lineText.TrimEnd('\r');

            var status = LineStatus(lineStart, lineEnd, trimmed, ranges);
            sb.Append($"<tr class=\"{status}\"><td class=\"ln\">{i + 1}</td><td class=\"src\"><pre>");
            sb.Append(HttpUtility.HtmlEncode(trimmed));
            sb.Append("</pre></td></tr>");
        }
        sb.AppendLine("</tbody></table>");
    }

    private static string LineStatus(int lineStart, int lineEnd, string lineText, IReadOnlyList<CoverageRange> ranges)
    {
        if (string.IsNullOrWhiteSpace(lineText))
            return "neutral";

        var anyHit = false;
        var anyTouched = false;
        foreach (var r in ranges)
        {
            if (r.StartOffset < lineEnd && r.EndOffset > lineStart)
            {
                anyTouched = true;
                if (r.Count > 0) { anyHit = true; break; }
            }
        }

        if (anyHit) return "covered";
        if (anyTouched) return "uncovered";
        return "neutral";
    }

    private static string GaugeHtml(string label, int covered, int total, double pct)
    {
        var cls = PercentClass(pct);
        return $"<div class=\"gauge\"><div class=\"label\">{HttpUtility.HtmlEncode(label)}</div><div class=\"value {cls}\">{pct:F1}%</div><div class=\"detail\">{covered} / {total}</div></div>";
    }

    private static string PercentClass(double pct) => pct switch
    {
        > 80.0 => "good",
        >= 50.0 => "warn",
        _ => "bad",
    };

    private static string MakeFileName(string url, string extPrefix)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
        var hex = Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
        var safe = new string(url.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_').Take(40).ToArray());
        if (string.IsNullOrEmpty(safe)) safe = "file";
        return $"{extPrefix}-{safe}-{hex}.html";
    }

    private const string SharedCss = """
body { font-family: system-ui, sans-serif; margin: 0; padding: 2rem; background: #fafafa; color: #24292e; }
h1 { margin-top: 0; }
.gauges { display: flex; gap: 1rem; margin-bottom: 1.5rem; }
.gauge { background: #fff; border: 1px solid #e1e4e8; border-radius: 6px; padding: 1rem 1.25rem; min-width: 160px; }
.gauge .label { color: #6a737d; font-size: 0.85rem; }
.gauge .value { font-size: 1.6rem; font-weight: 700; }
.gauge .detail { color: #6a737d; font-size: 0.85rem; }
.good { color: #22863a; background-color: #dcffe4; }
.warn { color: #735c0f; background-color: #fff8c5; }
.bad { color: #cb2431; background-color: #ffeef0; }
table.file-tree { width: 100%; border-collapse: collapse; background: #fff; }
table.file-tree th, table.file-tree td { padding: 0.5rem 0.75rem; border-bottom: 1px solid #e1e4e8; text-align: left; }
table.file-tree th { background: #f6f8fa; }
table.file-tree td.bar { width: 220px; }
table.file-tree td.bar span { display: block; height: 10px; border-radius: 4px; }
table.source { font-family: ui-monospace, Menlo, Consolas, monospace; font-size: 0.85rem; border-collapse: collapse; width: 100%; background: #fff; }
table.source td { padding: 0 0.5rem; vertical-align: top; }
table.source td.ln { color: #959da5; text-align: right; user-select: none; width: 4rem; border-right: 1px solid #e1e4e8; }
table.source pre { margin: 0; white-space: pre-wrap; word-break: break-all; }
table.source tr.covered { background: #e6ffec; }
table.source tr.uncovered { background: #ffebe9; }
table.source tr.neutral { background: #fff; }
.diagnostic { padding: 0.5rem 0.75rem; background: #fff8c5; border-left: 4px solid #d4a72c; }
footer { margin-top: 2rem; color: #6a737d; font-size: 0.8rem; }
""";

    private sealed record FileEntry(string Kind, string Url, string RelativePath, FileCoverageStats Stats);
}
