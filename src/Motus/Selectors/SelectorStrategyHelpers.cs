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
    /// <remarks>
    /// The expression runs in the given frame's execution context, so a bare <c>document</c> in
    /// the strategy's JavaScript refers to that frame's document. A strategy therefore scopes to
    /// the frame it was handed without writing any frame-aware JavaScript of its own.
    /// </remarks>
    internal static async Task<IReadOnlyList<IElementHandle>> EvalToHandlesAsync(
        IFrame frame, string js, CancellationToken ct)
    {
        var page = GetPage(frame);
        var session = page.SessionFor(frame);

        var result = await session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: js,
                ReturnByValue: false,
                AwaitPromise: false,
                ContextId: page.GetSelectorContextId(frame)),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            ct).ConfigureAwait(false);

        if (result.ExceptionDetails is not null)
            throw new InvalidOperationException(
                $"Selector resolution failed: {result.ExceptionDetails.Text}");

        if (result.Result.ObjectId is null)
            return [];

        var props = await session.SendAsync(
            "Runtime.getProperties",
            new RuntimeGetPropertiesParams(result.Result.ObjectId, OwnProperties: true),
            CdpJsonContext.Default.RuntimeGetPropertiesParams,
            CdpJsonContext.Default.RuntimeGetPropertiesResult,
            ct).ConfigureAwait(false);

        var handles = new List<IElementHandle>();
        foreach (var prop in props.Result)
        {
            if (int.TryParse(prop.Name, out _) && prop.Value?.ObjectId is not null)
                handles.Add(new ElementHandle(session, prop.Value.ObjectId));
        }

        return handles;
    }

    /// <summary>
    /// Resolves a backend DOM node ID to an ElementHandle via DOM.resolveNode.
    /// </summary>
    /// <remarks>
    /// Backend node IDs are assigned per document but are addressable across every document a
    /// session can see, so this needs no execution context of its own. It still takes the frame
    /// so the handle is bound to the session that owns it.
    /// </remarks>
    internal static async Task<ElementHandle> ResolveNodeToHandleAsync(
        IFrame frame, long backendNodeId, CancellationToken ct)
    {
        var session = GetPage(frame).SessionFor(frame);

        var resolved = await session.SendAsync(
            "DOM.resolveNode",
            new DomResolveNodeParams(BackendNodeId: (int)backendNodeId),
            CdpJsonContext.Default.DomResolveNodeParams,
            CdpJsonContext.Default.DomResolveNodeResult,
            ct).ConfigureAwait(false);

        if (resolved.Object.ObjectId is null)
            throw new InvalidOperationException(
                $"DOM.resolveNode returned no objectId for backendNodeId {backendNodeId}.");

        return new ElementHandle(session, resolved.Object.ObjectId);
    }

    /// <summary>
    /// Resolves the given frame's document element to a remote object ID, for protocol commands
    /// that scope by node rather than by execution context. Returns null for the page default.
    /// </summary>
    internal static async Task<string?> ResolveFrameDocumentObjectIdAsync(
        IFrame frame, CancellationToken ct)
    {
        var page = GetPage(frame);
        var contextId = page.GetSelectorContextId(frame);
        if (contextId is null)
            return null;

        var result = await page.SessionFor(frame).SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: "document.documentElement",
                ReturnByValue: false,
                AwaitPromise: false,
                ContextId: contextId),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            ct).ConfigureAwait(false);

        return result.ExceptionDetails is null ? result.Result.ObjectId : null;
    }

    /// <summary>
    /// Extracts the Page instance from an IFrame (safe cast since all strategies run in-process).
    /// </summary>
    internal static Page GetPage(IFrame frame)
        => (Page)frame.Page;
}
