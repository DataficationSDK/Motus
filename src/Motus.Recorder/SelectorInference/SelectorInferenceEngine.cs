using Motus.Abstractions;

namespace Motus.Recorder.SelectorInference;

/// <summary>
/// Determines the best unique selector for an element at a given coordinate using
/// registered selector strategies in priority order.
/// </summary>
internal sealed class SelectorInferenceEngine
{
    private readonly IReadOnlyList<ISelectorStrategy> _strategies;
    private readonly Page _page;
    private readonly SelectorInferenceOptions _options;

    internal SelectorInferenceEngine(
        IReadOnlyList<ISelectorStrategy> strategiesByPriority,
        Page page,
        SelectorInferenceOptions? options = null)
    {
        _strategies = strategiesByPriority;
        _page = page;
        _options = options ?? new SelectorInferenceOptions();
    }

    /// <summary>
    /// Infers a unique selector for the element at the given coordinates.
    /// Returns null if no unambiguous selector can be determined.
    /// </summary>
    internal async Task<string?> InferAsync(double x, double y, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.InferenceTimeout);
        var linkedCt = timeoutCts.Token;

        try
        {
            // Hit-test to get the backend node at the coordinates
            var nodeResult = await _page.Session.SendAsync(
                "DOM.getNodeForLocation",
                new DomGetNodeForLocationParams((int)x, (int)y),
                CdpJsonContext.Default.DomGetNodeForLocationParams,
                CdpJsonContext.Default.DomGetNodeForLocationResult,
                linkedCt);

            // Resolve to an ElementHandle
            var element = await SelectorStrategyHelpers.ResolveNodeToHandleAsync(
                _page, nodeResult.BackendNodeId, linkedCt);

            // Try each strategy in priority order
            foreach (var strategy in _strategies)
            {
                var selector = await strategy.GenerateSelector(element, linkedCt);
                if (selector is null || selector.Length > _options.MaxSelectorLength)
                    continue;

                var matches = await strategy.ResolveAsync(
                    selector, _page.MainFrame, pierceShadow: true, linkedCt);

                if (matches.Count == 1)
                    return selector;
            }

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }
}
