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
    /// When a <paramref name="targetId"/> is provided, retrieves the element captured
    /// at event time (surviving DOM mutations). Falls back to coordinate-based hit-testing.
    /// </summary>
    internal async Task<string?> InferAsync(double x, double y, int? targetId, CancellationToken ct)
    {
        var result = await InferDetailedAsync(x, y, targetId, ct).ConfigureAwait(false);
        return result.Selector;
    }

    /// <summary>
    /// Same as <see cref="InferAsync"/> but also returns the CDP backend node ID and
    /// the locator factory method name, for manifest/fingerprint emission.
    /// </summary>
    internal async Task<SelectorInferenceResult> InferDetailedAsync(
        double x, double y, int? targetId, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_options.InferenceTimeout);
        var linkedCt = timeoutCts.Token;

        try
        {
            ElementHandle? element = null;
            int? backendNodeId = null;

            if (targetId is not null)
            {
                element = await ResolveTargetAsync(targetId.Value, linkedCt);
            }

            if (element is null)
            {
                var coord = await ResolveByCoordinatesAsync(x, y, linkedCt);
                element = coord.Handle;
                backendNodeId = coord.BackendNodeId;
            }

            if (backendNodeId is null)
            {
                backendNodeId = await TryGetBackendNodeIdAsync(element!, linkedCt);
            }

            foreach (var strategy in _strategies)
            {
                try
                {
                    var selector = await strategy.GenerateSelector(element!, linkedCt);
                    if (selector is null || selector.Length > _options.MaxSelectorLength)
                        continue;

                    var matches = await strategy.ResolveAsync(
                        selector, _page.MainFrame, pierceShadow: true, linkedCt);

                    if (matches.Count == 1)
                        return new SelectorInferenceResult(selector, "Locator", backendNodeId);
                }
                catch
                {
                    // Strategy failed; try next
                }
            }

            return new SelectorInferenceResult(null, null, backendNodeId);
        }
        catch (OperationCanceledException)
        {
            return new SelectorInferenceResult(null, null, null);
        }
        catch
        {
            return new SelectorInferenceResult(null, null, null);
        }
    }

    private async Task<ElementHandle?> ResolveTargetAsync(int targetId, CancellationToken ct)
    {
        try
        {
            var result = await _page.Session.SendAsync(
                "Runtime.evaluate",
                new RuntimeEvaluateParams(
                    Expression: $"window.__motus_get_target__({targetId})",
                    ReturnByValue: false,
                    AwaitPromise: false),
                CdpJsonContext.Default.RuntimeEvaluateParams,
                CdpJsonContext.Default.RuntimeEvaluateResult,
                ct).ConfigureAwait(false);

            if (result.ExceptionDetails is not null || result.Result.ObjectId is null)
                return null;

            return new ElementHandle(_page.Session, result.Result.ObjectId);
        }
        catch
        {
            return null;
        }
    }

    private async Task<CoordinateResolution> ResolveByCoordinatesAsync(double x, double y, CancellationToken ct)
    {
        var nodeResult = await _page.Session.SendAsync(
            "DOM.getNodeForLocation",
            new DomGetNodeForLocationParams((int)x, (int)y),
            CdpJsonContext.Default.DomGetNodeForLocationParams,
            CdpJsonContext.Default.DomGetNodeForLocationResult,
            ct);

        var handle = await SelectorStrategyHelpers.ResolveNodeToHandleAsync(
            _page, nodeResult.BackendNodeId, ct);

        return new CoordinateResolution(handle, nodeResult.BackendNodeId);
    }

    private async Task<int?> TryGetBackendNodeIdAsync(ElementHandle element, CancellationToken ct)
    {
        try
        {
            var describe = await _page.Session.SendAsync(
                "DOM.describeNode",
                new DomDescribeNodeParams(ObjectId: element.ObjectId),
                CdpJsonContext.Default.DomDescribeNodeParams,
                CdpJsonContext.Default.DomDescribeNodeResult,
                ct).ConfigureAwait(false);

            return describe.Node.BackendNodeId;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct CoordinateResolution(ElementHandle Handle, int BackendNodeId);
}

internal sealed record SelectorInferenceResult(
    string? Selector,
    string? LocatorMethod,
    int? BackendNodeId);
