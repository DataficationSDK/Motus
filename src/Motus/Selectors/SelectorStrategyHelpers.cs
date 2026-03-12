using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Shared helpers for built-in selector strategies.
/// </summary>
internal static class SelectorStrategyHelpers
{
    /// <summary>
    /// Evaluates a JS expression that returns an array of elements (ReturnByValue: false),
    /// then enumerates via Runtime.getProperties to build a list of ElementHandles.
    /// </summary>
    internal static async Task<IReadOnlyList<IElementHandle>> EvalToHandlesAsync(
        Page page, string js, CancellationToken ct)
    {
        var result = await page.Session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: js,
                ReturnByValue: false,
                AwaitPromise: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            ct);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Selector resolution failed: {result.ExceptionDetails.Text}");

        if (result.Result.ObjectId is null)
            return [];

        var props = await page.Session.SendAsync(
            "Runtime.getProperties",
            new RuntimeGetPropertiesParams(result.Result.ObjectId, OwnProperties: true),
            CdpJsonContext.Default.RuntimeGetPropertiesParams,
            CdpJsonContext.Default.RuntimeGetPropertiesResult,
            ct);

        var handles = new List<IElementHandle>();
        foreach (var prop in props.Result)
        {
            if (int.TryParse(prop.Name, out _) && prop.Value?.ObjectId is not null)
                handles.Add(new ElementHandle(page.Session, prop.Value.ObjectId));
        }

        return handles;
    }

    /// <summary>
    /// Resolves a backend DOM node ID to an ElementHandle via DOM.resolveNode.
    /// </summary>
    internal static async Task<ElementHandle> ResolveNodeToHandleAsync(
        Page page, long backendNodeId, CancellationToken ct)
    {
        var resolved = await page.Session.SendAsync(
            "DOM.resolveNode",
            new DomResolveNodeParams(BackendNodeId: (int)backendNodeId),
            CdpJsonContext.Default.DomResolveNodeParams,
            CdpJsonContext.Default.DomResolveNodeResult,
            ct);

        if (resolved.Object.ObjectId is null)
            throw new InvalidOperationException(
                $"DOM.resolveNode returned no objectId for backendNodeId {backendNodeId}.");

        return new ElementHandle(page.Session, resolved.Object.ObjectId);
    }

    /// <summary>
    /// Extracts the Page instance from an IFrame (safe cast since all strategies run in-process).
    /// </summary>
    internal static Page GetPage(IFrame frame)
        => (Page)frame.Page;
}
