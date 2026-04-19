using System.Text.Json;
using Motus.Abstractions;
using InternalPage = Motus.Page;

namespace Motus.Runner.Services.SelectorRepair;

/// <summary>
/// Resolves a CSS selector on the live page and asks the browser to draw the
/// devtools-style highlight overlay via <c>DOM.highlightNode</c>. CDP-only;
/// silently no-ops on BiDi transports.
/// </summary>
internal static class HighlightHelper
{
    private static readonly DomHighlightConfig DefaultConfig = new(
        ShowInfo: true,
        ContentColor: new DomRgba(R: 111, G: 168, B: 220, A: 0.66),
        BorderColor: new DomRgba(R: 0, G: 122, B: 204, A: 1.0));

    internal static async Task HighlightAsync(IPage page, string cssSelector, CancellationToken ct)
    {
        if (page is not InternalPage internalPage)
            return;

        var session = internalPage.Session;
        if ((session.Capabilities & MotusCapabilities.AllCdp) == 0)
            return;

        var encoded = JsonSerializer.Serialize(cssSelector);
        var evalResult = await session.SendAsync(
            "Runtime.evaluate",
            new RuntimeEvaluateParams(
                Expression: $"document.querySelector({encoded})",
                ReturnByValue: false),
            CdpJsonContext.Default.RuntimeEvaluateParams,
            CdpJsonContext.Default.RuntimeEvaluateResult,
            ct).ConfigureAwait(false);

        var objectId = evalResult.Result.ObjectId;
        if (objectId is null)
            return;

        var describe = await session.SendAsync(
            "DOM.describeNode",
            new DomDescribeNodeParams(ObjectId: objectId),
            CdpJsonContext.Default.DomDescribeNodeParams,
            CdpJsonContext.Default.DomDescribeNodeResult,
            ct).ConfigureAwait(false);

        var backendNodeId = describe.Node.BackendNodeId;
        if (backendNodeId is null or 0)
            return;

        await session.SendAsync(
            "DOM.highlightNode",
            new DomHighlightNodeParams(
                HighlightConfig: DefaultConfig,
                BackendNodeId: backendNodeId),
            CdpJsonContext.Default.DomHighlightNodeParams,
            CdpJsonContext.Default.DomHighlightNodeResult,
            ct).ConfigureAwait(false);
    }

    internal static async Task HideAsync(IPage page, CancellationToken ct)
    {
        if (page is not InternalPage internalPage)
            return;

        var session = internalPage.Session;
        if ((session.Capabilities & MotusCapabilities.AllCdp) == 0)
            return;

        await session.SendAsync<DomHideHighlightResult>(
            "DOM.hideHighlight",
            CdpJsonContext.Default.DomHideHighlightResult,
            ct).ConfigureAwait(false);
    }
}
