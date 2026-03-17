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

        // Listener pass: discover elements with directly-attached JS event handlers
        var allElements = new List<PageElementInfo>(elements);

        if (_options.DetectEventListeners)
        {
            try
            {
                var listenerElements = await DiscoverListenerElementsAsync(page, linkedCt);
                allElements.AddRange(listenerElements);
            }
            catch (OperationCanceledException) { }
            catch { /* Listener pass failed; proceed with semantic results only */ }
        }

        if (allElements.Count == 0)
            return [];

        // Derive member names
        var names = MemberNameDeriver.DeriveNames(allElements);

        // Reorder strategies based on priority override
        var orderedStrategies = SelectorStrategyOrdering.Reorder(
            _strategies, _options.SelectorPriority);

        // Per-element: resolve handle, infer selector
        var results = new List<DiscoveredElement>(allElements.Count);
        for (var i = 0; i < allElements.Count; i++)
        {
            var info = allElements[i];
            string? selector = null;

            // Elements from listener pass have negative ElementIndex as a sentinel
            var handleJs = info.ElementIndex >= 0
                ? $"document.querySelectorAll('{InteractiveSelector}')[{info.ElementIndex}]"
                : $"window.__mtusCandidates[{-(info.ElementIndex + 1)}]";

            try
            {
                selector = await InferSelectorForElement(
                    page, handleJs, orderedStrategies, linkedCt);
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
        string handleJs,
        IReadOnlyList<ISelectorStrategy> strategies,
        CancellationToken ct)
    {
        var internalPage = (Page)page;
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

    private static readonly HashSet<string> InteractionEventTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "click", "pointerdown", "pointerup", "mousedown", "mouseup",
        "keydown", "keyup", "change", "input", "submit", "touchstart"
    };

    private async Task<IReadOnlyList<PageElementInfo>> DiscoverListenerElementsAsync(
        IPage page, CancellationToken ct)
    {
        var candidatesJson = await page.EvaluateAsync<JsonElement>(EventListenerCandidateScript);
        var candidates = DeserializeElements(candidatesJson);

        if (candidates.Count == 0)
            return [];

        var internalPage = (Page)page;
        var surviving = new List<PageElementInfo>();

        for (var i = 0; i < candidates.Count; i++)
        {
            var handleJs = $"[window.__mtusCandidates[{i}]]";
            var handles = await SelectorStrategyHelpers.EvalToHandlesAsync(internalPage, handleJs, ct);

            if (handles.Count == 0)
                continue;

            var objectId = ((ElementHandle)handles[0]).ObjectId;
            var listeners = await GetEventListenersAsync(internalPage, objectId, ct);

            if (listeners.Any(l => InteractionEventTypes.Contains(l.Type)))
            {
                // Use negative index as sentinel: -(candidateIndex + 1)
                surviving.Add(candidates[i] with { ElementIndex = -(i + 1) });
            }
        }

        return surviving;
    }

    private static async Task<DomDebuggerEventListener[]> GetEventListenersAsync(
        Page page, string objectId, CancellationToken ct)
    {
        var result = await page.Session.SendAsync(
            "DOMDebugger.getEventListeners",
            new DomDebuggerGetEventListenersParams(objectId, Depth: 0, Pierce: false),
            CdpJsonContext.Default.DomDebuggerGetEventListenersParams,
            CdpJsonContext.Default.DomDebuggerGetEventListenersResult,
            ct).ConfigureAwait(false);

        return result.Listeners;
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

    private const string EventListenerCandidateScript = """
        (() => {
            const semanticSelector = 'input:not([type="hidden"]):not([disabled]),' +
                'button:not([disabled]),' +
                'select:not([disabled]),' +
                'textarea:not([disabled]),' +
                'a[href],' +
                '[role="button"]:not([disabled])';
            const semanticSet = new Set(document.querySelectorAll(semanticSelector));

            const all = document.querySelectorAll('*');
            const candidates = [];
            for (const el of all) {
                if (semanticSet.has(el)) continue;
                const rect = el.getBoundingClientRect();
                if (rect.width === 0 || rect.height === 0) continue;
                const style = getComputedStyle(el);
                if (style.display === 'none' || style.visibility === 'hidden') continue;
                const hasPointer = style.cursor === 'pointer';
                const hasTabindex = el.hasAttribute('tabindex');
                if (!hasPointer && !hasTabindex) continue;
                candidates.push(el);
            }

            // Parent/child dedup: if a candidate's parent is also a candidate, drop the child
            const candidateSet = new Set(candidates);
            const surviving = candidates.filter(el => {
                let parent = el.parentElement;
                while (parent) {
                    if (candidateSet.has(parent)) return false;
                    parent = parent.parentElement;
                }
                return true;
            });

            window.__mtusCandidates = surviving;

            return surviving.map(el => {
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
                    formIndex: null
                };
            });
        })()
        """;
}
