using System.Text.Json;
using Motus;
using Motus.Abstractions;
using Motus.Cli.Commands;
using Motus.Selectors;

namespace Motus.Cli.Services;

/// <summary>
/// Orchestrates <c>motus check-selectors</c>: parse C# sources for locator calls,
/// navigate a real browser to the relevant page(s), record whether each selector
/// still resolves to exactly one element, and emit a colored summary table plus
/// optional JSON output. Returns the command's process exit code.
/// </summary>
internal sealed class CheckSelectorsRunner
{
    private readonly TextWriter _stdout;
    private readonly TextWriter _stderr;
    private readonly bool _useColor;

    internal CheckSelectorsRunner(
        TextWriter? stdout = null, TextWriter? stderr = null, bool? useColor = null)
    {
        _stdout = stdout ?? Console.Out;
        _stderr = stderr ?? Console.Error;
        _useColor = useColor ?? !Console.IsOutputRedirected;
    }

    internal Task<int> RunAsync(
        string glob,
        string? manifestPath,
        string? baseUrl,
        bool ci,
        string? jsonOutputPath,
        CancellationToken ct) =>
        RunAsync(glob, manifestPath, baseUrl, ci, jsonOutputPath, fix: false, backup: true, ct);

    internal async Task<int> RunAsync(
        string glob,
        string? manifestPath,
        string? baseUrl,
        bool ci,
        string? jsonOutputPath,
        bool fix,
        bool backup,
        CancellationToken ct)
    {
        // 1. Parse selectors from C# sources.
        SelectorParseResult parseResult;
        try
        {
            parseResult = await SelectorParser.ParseGlobAsync(
                glob, Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _stderr.WriteLine($"error: failed to parse sources: {ex.Message}");
            return 2;
        }

        foreach (var w in parseResult.Warnings)
            _stderr.WriteLine($"warning: {w.SourceFile}:{w.SourceLine}: {w.Message}");

        if (parseResult.Selectors.Count == 0)
        {
            _stdout.WriteLine("No locator calls found. Nothing to check.");
            return 0;
        }

        // 2. Load manifest if supplied.
        SelectorManifest? manifest = null;
        if (manifestPath is not null)
        {
            try
            {
                var json = await File.ReadAllTextAsync(manifestPath, ct).ConfigureAwait(false);
                manifest = JsonSerializer.Deserialize(
                    json, SelectorManifestJsonContext.Default.SelectorManifest);
                if (manifest is null)
                {
                    _stderr.WriteLine($"error: manifest '{manifestPath}' deserialized to null.");
                    return 2;
                }
            }
            catch (Exception ex)
            {
                _stderr.WriteLine($"error: failed to load manifest '{manifestPath}': {ex.Message}");
                return 2;
            }
        }

        var manifestLookup = manifest is null
            ? null
            : BuildManifestLookup(manifest);

        // 3. Build URL groups + the initial result list (skipped rows already materialized).
        var results = new List<SelectorCheckResult>();
        var groups = new Dictionary<string, List<(ParsedSelector Parsed, SelectorEntry? Entry)>>(StringComparer.Ordinal);

        foreach (var selector in parseResult.Selectors)
        {
            if (selector.IsInterpolated)
            {
                results.Add(ToSkipped(selector,
                    pageUrl: "",
                    note: "interpolated selector cannot be validated statically"));
                continue;
            }

            if (manifestLookup is not null)
            {
                if (!manifestLookup.TryGetValue((selector.Selector, selector.LocatorMethod), out var entry))
                {
                    results.Add(ToSkipped(selector,
                        pageUrl: "",
                        note: "no manifest entry; cannot determine page URL"));
                    continue;
                }

                AddToGroup(groups, entry.PageUrl, selector, entry);
            }
            else
            {
                AddToGroup(groups, baseUrl!, selector, entry: null);
            }
        }

        if (groups.Count == 0)
        {
            PrintAndWriteOutputs(results, jsonOutputPath, rewriteReport: null);
            return ExitCodeFor(results, ci);
        }

        // 4. Launch browser and check each URL group.
        IBrowser? browser = null;
        try
        {
            try
            {
                var launchOptions = new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = BrowserPathHelper.Resolve(),
                };
                browser = await MotusLauncher.LaunchAsync(launchOptions, ct).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                _stderr.WriteLine("error: no browser found. Run 'motus install' first.");
                return 2;
            }
            catch (Exception ex)
            {
                _stderr.WriteLine($"error: failed to launch browser: {ex.Message}");
                return 2;
            }

            var context = await browser.NewContextAsync().ConfigureAwait(false);
            var page = await context.NewPageAsync().ConfigureAwait(false);

            foreach (var (url, items) in groups)
            {
                try
                {
                    await page.GotoAsync(url, new NavigationOptions { WaitUntil = WaitUntil.Load })
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    foreach (var item in items)
                    {
                        results.Add(ToSkipped(item.Parsed,
                            pageUrl: url,
                            note: $"navigation failed: {ex.Message}"));
                    }
                    continue;
                }

                foreach (var (parsed, entry) in items)
                {
                    ct.ThrowIfCancellationRequested();
                    results.Add(await CheckOneAsync(page, parsed, entry, url, ct).ConfigureAwait(false));
                }
            }
        }
        finally
        {
            if (browser is not null)
                await browser.CloseAsync().ConfigureAwait(false);
        }

        RewriteReport? rewriteReport = null;
        if (fix)
            rewriteReport = SelectorRewriter.Apply(results, backup, ct);

        PrintAndWriteOutputs(results, jsonOutputPath, rewriteReport);
        return ExitCodeFor(results, ci);
    }

    private async Task<SelectorCheckResult> CheckOneAsync(
        IPage page, ParsedSelector parsed, SelectorEntry? entry, string pageUrl, CancellationToken ct)
    {
        int count;
        try
        {
            var locator = LocatorDispatcher.Dispatch(page, parsed);
            count = await locator.CountAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new SelectorCheckResult(
                SelectorCheckStatus.Broken,
                parsed.Selector, parsed.LocatorMethod, parsed.SourceFile, parsed.SourceLine,
                pageUrl, MatchCount: 0, Suggestion: null, Note: $"dispatch error: {ex.Message}");
        }

        var status = count switch
        {
            0 => SelectorCheckStatus.Broken,
            1 => SelectorCheckStatus.Healthy,
            _ => SelectorCheckStatus.Ambiguous,
        };

        string? topSuggestion = null;
        IReadOnlyList<RepairSuggestion>? suggestions = null;
        if (status == SelectorCheckStatus.Broken && entry is not null)
        {
            var match = await FingerprintScanner.FindMatchAsync(page, entry.Fingerprint, ct)
                .ConfigureAwait(false);
            if (match is not null)
            {
                suggestions = await RepairSuggestionPipeline.BuildAsync(
                    page, entry.Fingerprint, match, ct).ConfigureAwait(false);
                if (suggestions.Count > 0)
                    topSuggestion = suggestions[0].Replacement;
            }
        }

        return new SelectorCheckResult(
            status,
            parsed.Selector, parsed.LocatorMethod, parsed.SourceFile, parsed.SourceLine,
            pageUrl, MatchCount: count, Suggestion: topSuggestion, Note: null)
        {
            Suggestions = suggestions,
        };
    }

    private void PrintAndWriteOutputs(
        List<SelectorCheckResult> results, string? jsonOutputPath, RewriteReport? rewriteReport)
    {
        SelectorCheckTablePrinter.Print(results, _stdout, _useColor, rewriteReport);

        if (jsonOutputPath is not null)
        {
            try
            {
                var dir = Path.GetDirectoryName(jsonOutputPath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(
                    results, CheckResultsJsonContext.Default.ListSelectorCheckResult);
                File.WriteAllText(jsonOutputPath, json);
            }
            catch (Exception ex)
            {
                _stderr.WriteLine($"warning: failed to write JSON output to '{jsonOutputPath}': {ex.Message}");
            }
        }
    }

    private static int ExitCodeFor(IReadOnlyList<SelectorCheckResult> results, bool ci)
    {
        if (!ci)
            return 0;
        foreach (var r in results)
        {
            if (r.Status == SelectorCheckStatus.Broken && !r.Fixed)
                return 1;
        }
        return 0;
    }

    private static Dictionary<(string Selector, string LocatorMethod), SelectorEntry> BuildManifestLookup(
        SelectorManifest manifest)
    {
        var map = new Dictionary<(string, string), SelectorEntry>();
        foreach (var entry in manifest.Entries)
            map[(entry.Selector, entry.LocatorMethod)] = entry;
        return map;
    }

    private static void AddToGroup(
        Dictionary<string, List<(ParsedSelector, SelectorEntry?)>> groups,
        string url,
        ParsedSelector parsed,
        SelectorEntry? entry)
    {
        if (!groups.TryGetValue(url, out var list))
        {
            list = new List<(ParsedSelector, SelectorEntry?)>();
            groups[url] = list;
        }
        list.Add((parsed, entry));
    }

    private static SelectorCheckResult ToSkipped(ParsedSelector s, string pageUrl, string note) =>
        new(SelectorCheckStatus.Skipped,
            s.Selector, s.LocatorMethod, s.SourceFile, s.SourceLine,
            pageUrl, MatchCount: 0, Suggestion: null, Note: note);
}
