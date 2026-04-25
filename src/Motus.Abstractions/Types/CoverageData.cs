namespace Motus.Abstractions;

/// <summary>
/// A contiguous byte-offset range within a script source with an associated execution count.
/// </summary>
/// <param name="StartOffset">Start byte offset (inclusive) within the source text.</param>
/// <param name="EndOffset">End byte offset (exclusive) within the source text.</param>
/// <param name="Count">Number of times this range was executed. Zero means uncovered.</param>
public sealed record CoverageRange(int StartOffset, int EndOffset, int Count);

/// <summary>
/// CSS rule usage entry within a stylesheet source.
/// </summary>
/// <param name="StartOffset">Start byte offset (inclusive) within the stylesheet source.</param>
/// <param name="EndOffset">End byte offset (exclusive) within the stylesheet source.</param>
/// <param name="Used">True if the rule was matched/applied during the session.</param>
public sealed record CssRuleUsage(int StartOffset, int EndOffset, bool Used);

/// <summary>
/// Per-file coverage statistics.
/// </summary>
/// <param name="TotalLines">Total countable units (lines for scripts, rules for stylesheets).</param>
/// <param name="CoveredLines">Number of units that were covered.</param>
/// <param name="Percentage">Coverage percentage (0-100). Zero if <paramref name="TotalLines"/> is zero.</param>
public sealed record FileCoverageStats(int TotalLines, int CoveredLines, double Percentage);

/// <summary>
/// JavaScript coverage data for a single script URL, aggregated across all sessions.
/// </summary>
/// <param name="Url">The script URL (or script ID when no URL is available).</param>
/// <param name="Source">Full script source text used to compute line-level coverage.</param>
/// <param name="Ranges">Sorted, non-overlapping covered ranges with execution counts.</param>
/// <param name="Stats">Line-level coverage stats derived from <paramref name="Ranges"/> and <paramref name="Source"/>.</param>
public sealed record ScriptCoverage(
    string Url,
    string Source,
    IReadOnlyList<CoverageRange> Ranges,
    FileCoverageStats Stats);

/// <summary>
/// CSS coverage data for a single stylesheet URL, aggregated across all sessions.
/// Note: <paramref name="Source"/> is included beyond the spec because line-level
/// computation requires the source text.
/// </summary>
/// <param name="Url">The stylesheet URL (or stylesheet ID when no URL is available).</param>
/// <param name="Source">Full stylesheet source text.</param>
/// <param name="Rules">Per-rule usage entries.</param>
/// <param name="Stats">Rule-level coverage stats (Used rules / total rules).</param>
public sealed record StylesheetCoverage(
    string Url,
    string Source,
    IReadOnlyList<CssRuleUsage> Rules,
    FileCoverageStats Stats);

/// <summary>
/// Aggregated summary across all scripts and stylesheets in a coverage snapshot.
/// </summary>
/// <param name="TotalLines">Total JS lines across all scripts.</param>
/// <param name="CoveredLines">Covered JS lines across all scripts.</param>
/// <param name="LinePercentage">JS line coverage percentage (0-100).</param>
/// <param name="TotalCssRules">Total CSS rules across all stylesheets.</param>
/// <param name="UsedCssRules">Used CSS rules across all stylesheets.</param>
/// <param name="CssPercentage">CSS rule coverage percentage (0-100).</param>
public sealed record CoverageSummary(
    int TotalLines,
    int CoveredLines,
    double LinePercentage,
    int TotalCssRules,
    int UsedCssRules,
    double CssPercentage);

/// <summary>
/// A snapshot of JavaScript and CSS code coverage data collected during a page session.
/// </summary>
/// <param name="Scripts">Per-script JavaScript coverage entries.</param>
/// <param name="Stylesheets">Per-stylesheet CSS coverage entries.</param>
/// <param name="Summary">Aggregated coverage summary.</param>
/// <param name="CollectedAtUtc">UTC timestamp when the snapshot was collected.</param>
/// <param name="DiagnosticMessage">
/// Optional message when coverage could not be fully collected
/// (e.g. transport does not support the CDP Profiler/CSS domains).
/// </param>
public sealed record CoverageData(
    IReadOnlyList<ScriptCoverage> Scripts,
    IReadOnlyList<StylesheetCoverage> Stylesheets,
    CoverageSummary Summary,
    DateTime CollectedAtUtc,
    string? DiagnosticMessage = null);
