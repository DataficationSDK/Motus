using System.Text.Json;
using Motus;
using Motus.Abstractions;

namespace Motus.Recorder.PageAnalysis;

/// <summary>
/// Crawls a live page's DOM to discover interactive elements, infers selectors,
/// and derives C# member names for POM generation.
/// </summary>
public sealed class PageAnalysisEngine
{
    private readonly IReadOnlyList<ISelectorStrategy> _strategies;
    private readonly PageAnalysisOptions _options;

    public PageAnalysisEngine(
        IReadOnlyList<ISelectorStrategy> strategies,
        PageAnalysisOptions? options = null)
    {
        _strategies = strategies;
        _options = options ?? new PageAnalysisOptions();
    }

    /// <summary>
    /// Creates a <see cref="PageAnalysisEngine"/> by extracting registered selector strategies
    /// from the page's browser context. Use this when you do not have direct access to the
    /// internal strategy registry.
    /// </summary>
    public static PageAnalysisEngine Create(IPage page, PageAnalysisOptions? options = null)
    {
        var internalPage = (Page)page;
        var strategies = internalPage.ContextInternal.SelectorStrategies.GetAllByPriority();
        return new PageAnalysisEngine(strategies, options);
    }

    /// <summary>
    /// Discovers all interactive elements on the page, infers selectors, and returns
    /// a list of <see cref="DiscoveredElement"/> ready for code emission.
    /// </summary>
    public async Task<IReadOnlyList<DiscoveredElement>> AnalyzeAsync(
        IPage page, CancellationToken ct = default)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.InferenceTimeout);
        var linkedCt = timeoutCts.Token;

        // Single JS evaluation to discover all interactive elements
        var elementsJson = await page.EvaluateAsync<JsonElement>(DomCrawlScript);
        var elements = DeserializeElements(elementsJson);

        if (elements.Count == 0)
            return [];

        // Derive member names
        var names = MemberNameDeriver.DeriveNames(elements);

        // Reorder strategies based on priority override
        var orderedStrategies = SelectorStrategyOrdering.Reorder(
            _strategies, _options.SelectorPriority);

        // Per-element: resolve handle, infer selector
        var results = new List<DiscoveredElement>(elements.Count);
        for (var i = 0; i < elements.Count; i++)
        {
            var info = elements[i];
            string? selector = null;

            try
            {
                selector = await InferSelectorForElement(
                    page, info.ElementIndex, orderedStrategies, linkedCt);
            }
            catch (OperationCanceledException)
            {
                // Timeout; remaining elements get null selectors
            }
            catch
            {
                // Inference failed for this element
            }

            results.Add(new DiscoveredElement(info, selector, names[i]));
        }

        return results;
    }

    private async Task<string?> InferSelectorForElement(
        IPage page,
        int elementIndex,
        IReadOnlyList<ISelectorStrategy> strategies,
        CancellationToken ct)
    {
        // Resolve handle via positional querySelectorAll index
        var internalPage = (Page)page;
        var handleJs = $"document.querySelectorAll('{InteractiveSelector}')[{elementIndex}]";
        var handles = await SelectorStrategyHelpers.EvalToHandlesAsync(internalPage, handleJs, ct);

        if (handles.Count == 0)
            return null;

        var element = handles[0];

        // Try each strategy in priority order (same pattern as SelectorInferenceEngine)
        foreach (var strategy in strategies)
        {
            var selector = await strategy.GenerateSelector(element, ct);
            if (selector is null || selector.Length > _options.MaxSelectorLength)
                continue;

            var matches = await strategy.ResolveAsync(
                selector, page.MainFrame, pierceShadow: true, ct);

            if (matches.Count == 1)
                return selector;
        }

        return null;
    }

    private static IReadOnlyList<PageElementInfo> DeserializeElements(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
            return [];

        var results = new List<PageElementInfo>();
        var index = 0;

        foreach (var el in json.EnumerateArray())
        {
            results.Add(new PageElementInfo(
                Tag: el.GetProperty("tag").GetString() ?? "unknown",
                Type: GetStringOrNull(el, "type"),
                Id: GetStringOrNull(el, "id"),
                Name: GetStringOrNull(el, "name"),
                AriaLabel: GetStringOrNull(el, "ariaLabel"),
                Placeholder: GetStringOrNull(el, "placeholder"),
                Text: GetStringOrNull(el, "text"),
                Href: GetStringOrNull(el, "href"),
                Role: GetStringOrNull(el, "role"),
                DataTestId: GetStringOrNull(el, "dataTestId"),
                FormIndex: GetIntOrNull(el, "formIndex"),
                ElementIndex: index++));
        }

        return results;
    }

    private static string? GetStringOrNull(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.String)
        {
            var str = val.GetString();
            return string.IsNullOrWhiteSpace(str) ? null : str;
        }
        return null;
    }

    private static int? GetIntOrNull(JsonElement el, string property)
    {
        if (el.TryGetProperty(property, out var val) && val.ValueKind == JsonValueKind.Number)
            return val.GetInt32();
        return null;
    }

    private const string InteractiveSelector =
        "input:not([type=\\\"hidden\\\"]):not([disabled])," +
        "button:not([disabled])," +
        "select:not([disabled])," +
        "textarea:not([disabled])," +
        "a[href]," +
        "[role=\\\"button\\\"]:not([disabled])";

    internal const string DomCrawlScript = $$"""
        (() => {
            const selector = 'input:not([type="hidden"]):not([disabled]),' +
                'button:not([disabled]),' +
                'select:not([disabled]),' +
                'textarea:not([disabled]),' +
                'a[href],' +
                '[role="button"]:not([disabled])';
            const elements = document.querySelectorAll(selector);
            const forms = document.querySelectorAll('form');
            const formMap = new Map();
            forms.forEach((f, i) => formMap.set(f, i));

            return Array.from(elements).map(el => {
                let formIndex = null;
                const form = el.closest('form');
                if (form && formMap.has(form)) formIndex = formMap.get(form);

                const text = (el.textContent || '').trim().substring(0, 50);
                return {
                    tag: el.tagName.toLowerCase(),
                    type: el.getAttribute('type'),
                    id: el.id || null,
                    name: el.getAttribute('name'),
                    ariaLabel: el.getAttribute('aria-label'),
                    placeholder: el.getAttribute('placeholder'),
                    text: text || null,
                    href: el.getAttribute('href'),
                    role: el.getAttribute('role'),
                    dataTestId: el.getAttribute('data-testid'),
                    formIndex: formIndex
                };
            });
        })()
        """;
}
