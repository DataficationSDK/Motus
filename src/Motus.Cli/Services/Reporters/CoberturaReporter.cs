using System.Globalization;
using System.Xml.Linq;
using Motus.Abstractions;

namespace Motus.Cli.Services.Reporters;

/// <summary>
/// Writes aggregated coverage data as Cobertura-format XML for CI integration
/// (Azure DevOps, GitHub Actions, GitLab CI). Mirrors the Cobertura DTD: a single
/// &lt;coverage&gt; root with line-rate / lines-covered / lines-valid attributes,
/// one &lt;package&gt; per top-level URL host, one &lt;class&gt; per script or stylesheet.
/// </summary>
public sealed class CoberturaReporter(string outputPath) : ICoverageReporter
{
    public Task OnCoverageCollectedAsync(CoverageData coverage, TestInfo test) => Task.CompletedTask;

    public async Task OnCoverageRunEndAsync(CoverageData aggregated)
    {
        var summary = aggregated.Summary;
        var lineRate = summary.TotalLines > 0
            ? (double)summary.CoveredLines / summary.TotalLines
            : 0.0;

        var coverage = new XElement("coverage",
            new XAttribute("line-rate", FormatRate(lineRate)),
            new XAttribute("branch-rate", "0"),
            new XAttribute("lines-covered", summary.CoveredLines),
            new XAttribute("lines-valid", summary.TotalLines),
            new XAttribute("branches-covered", 0),
            new XAttribute("branches-valid", 0),
            new XAttribute("complexity", "0"),
            new XAttribute("version", "1.9"),
            new XAttribute("timestamp", new DateTimeOffset(aggregated.CollectedAtUtc, TimeSpan.Zero).ToUnixTimeSeconds()));

        coverage.Add(new XElement("sources", new XElement("source", ".")));

        var packagesElement = new XElement("packages");
        var grouped = aggregated.Scripts
            .Select(s => (Kind: "js", Url: s.Url, Source: s.Source, Stats: s.Stats, Lines: BuildScriptLines(s)))
            .Concat(aggregated.Stylesheets.Select(s => (Kind: "css", Url: s.Url, Source: s.Source, Stats: s.Stats, Lines: BuildStylesheetLines(s))))
            .GroupBy(x => GetPackageName(x.Url));

        foreach (var pkg in grouped.OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            int pkgTotal = 0, pkgCovered = 0;
            foreach (var f in pkg)
            {
                pkgTotal += f.Stats.TotalLines;
                pkgCovered += f.Stats.CoveredLines;
            }
            var pkgRate = pkgTotal > 0 ? (double)pkgCovered / pkgTotal : 0.0;

            var packageElement = new XElement("package",
                new XAttribute("name", pkg.Key),
                new XAttribute("line-rate", FormatRate(pkgRate)),
                new XAttribute("branch-rate", "0"),
                new XAttribute("complexity", "0"));

            var classes = new XElement("classes");
            foreach (var f in pkg.OrderBy(x => x.Url, StringComparer.Ordinal))
            {
                var classRate = f.Stats.TotalLines > 0
                    ? (double)f.Stats.CoveredLines / f.Stats.TotalLines
                    : 0.0;

                var classElement = new XElement("class",
                    new XAttribute("name", GetClassName(f.Url)),
                    new XAttribute("filename", f.Url),
                    new XAttribute("line-rate", FormatRate(classRate)),
                    new XAttribute("branch-rate", "0"),
                    new XAttribute("complexity", "0"));

                classElement.Add(new XElement("methods"));

                var linesElement = new XElement("lines");
                foreach (var (lineNumber, hits) in f.Lines)
                {
                    linesElement.Add(new XElement("line",
                        new XAttribute("number", lineNumber),
                        new XAttribute("hits", hits),
                        new XAttribute("branch", "false")));
                }
                classElement.Add(linesElement);
                classes.Add(classElement);
            }
            packageElement.Add(classes);
            packagesElement.Add(packageElement);
        }

        coverage.Add(packagesElement);

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XDocumentType("coverage", null, "http://cobertura.sourceforge.net/xml/coverage-04.dtd", null),
            coverage);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var fs = File.Create(outputPath);
        await using var writer = System.Xml.XmlWriter.Create(fs, new System.Xml.XmlWriterSettings
        {
            Indent = true,
            Async = true,
            Encoding = new System.Text.UTF8Encoding(false),
        });
        await doc.SaveAsync(writer, CancellationToken.None);
    }

    private static IReadOnlyList<(int LineNumber, int Hits)> BuildScriptLines(ScriptCoverage script)
    {
        if (string.IsNullOrEmpty(script.Source))
            return Array.Empty<(int, int)>();

        var lineStarts = new List<int> { 0 };
        for (int i = 0; i < script.Source.Length; i++)
            if (script.Source[i] == '\n') lineStarts.Add(i + 1);

        var totalLines = lineStarts.Count;
        if (lineStarts[^1] >= script.Source.Length) totalLines--;
        if (totalLines <= 0) return Array.Empty<(int, int)>();

        var result = new List<(int, int)>(totalLines);
        for (int i = 0; i < totalLines; i++)
        {
            var lineStart = lineStarts[i];
            var lineEnd = i + 1 < lineStarts.Count ? lineStarts[i + 1] - 1 : script.Source.Length;
            if (lineEnd <= lineStart) continue;

            int hits = 0;
            foreach (var r in script.Ranges)
            {
                if (r.Count > 0 && r.StartOffset < lineEnd && r.EndOffset > lineStart)
                {
                    hits = Math.Max(hits, r.Count);
                }
            }
            result.Add((i + 1, hits));
        }
        return result;
    }

    private static IReadOnlyList<(int LineNumber, int Hits)> BuildStylesheetLines(StylesheetCoverage sheet)
    {
        if (string.IsNullOrEmpty(sheet.Source) || sheet.Rules.Count == 0)
            return Array.Empty<(int, int)>();

        var result = new List<(int, int)>(sheet.Rules.Count);
        for (int i = 0; i < sheet.Rules.Count; i++)
        {
            var rule = sheet.Rules[i];
            // Translate the rule start offset to a 1-based line number in the source.
            int line = 1;
            for (int j = 0; j < rule.StartOffset && j < sheet.Source.Length; j++)
                if (sheet.Source[j] == '\n') line++;
            result.Add((line, rule.Used ? 1 : 0));
        }
        return result;
    }

    private static string GetPackageName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return string.IsNullOrEmpty(uri.Host) ? "default" : uri.Host;
        return "default";
    }

    private static string GetClassName(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var seg = uri.Segments.LastOrDefault()?.TrimEnd('/');
            if (!string.IsNullOrEmpty(seg)) return seg;
        }
        var idx = url.LastIndexOfAny(new[] { '/', '\\' });
        return idx >= 0 ? url[(idx + 1)..] : url;
    }

    private static string FormatRate(double rate) =>
        rate.ToString("F4", CultureInfo.InvariantCulture);
}
