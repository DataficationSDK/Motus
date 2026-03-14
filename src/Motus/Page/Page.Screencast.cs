using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    internal async Task StartScreencastAsync(
        string? format = null,
        int? quality = null,
        int? maxWidth = null,
        int? maxHeight = null,
        int? everyNthFrame = null,
        CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token, ct);
        await _session.SendAsync(
            "Page.startScreencast",
            new PageStartScreencastParams(format, quality, maxWidth, maxHeight, everyNthFrame),
            CdpJsonContext.Default.PageStartScreencastParams,
            CdpJsonContext.Default.PageStartScreencastResult,
            linked.Token).ConfigureAwait(false);
    }

    internal async Task StopScreencastAsync(CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token, ct);
        await _session.SendAsync(
            "Page.stopScreencast",
            CdpJsonContext.Default.PageStopScreencastResult,
            linked.Token).ConfigureAwait(false);
    }

    internal IAsyncEnumerable<PageScreencastFrameEvent> SubscribeScreencastFramesAsync(
        CancellationToken ct = default)
    {
        var token = ct == default ? _pageCts.Token : ct;
        return _session.SubscribeAsync(
            "Page.screencastFrame",
            CdpJsonContext.Default.PageScreencastFrameEvent,
            token);
    }

    internal async Task AckScreencastFrameAsync(int sessionId, CancellationToken ct = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token, ct);
        await _session.SendAsync(
            "Page.screencastFrameAck",
            new PageScreencastFrameAckParams(sessionId),
            CdpJsonContext.Default.PageScreencastFrameAckParams,
            CdpJsonContext.Default.PageScreencastFrameAckResult,
            linked.Token).ConfigureAwait(false);
    }

    internal async Task<DomGetBoxModelResult?> GetBoxModelAsync(
        string objectId,
        CancellationToken ct = default)
    {
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token, ct);
            return await _session.SendAsync(
                "DOM.getBoxModel",
                new DomGetBoxModelParams(ObjectId: objectId),
                CdpJsonContext.Default.DomGetBoxModelParams,
                CdpJsonContext.Default.DomGetBoxModelResult,
                linked.Token).ConfigureAwait(false);
        }
        catch (MotusProtocolException)
        {
            // Element may no longer exist (stale reference)
            return null;
        }
    }
}
