using Motus.Abstractions;
using InternalPage = Motus.Page;

namespace Motus.Runner.Services;

internal sealed class ScreencastService : IScreencastService, IAsyncDisposable
{
    private InternalPage? _page;
    private CancellationTokenSource? _pumpCts;
    private Task? _pumpTask;

    public string? LatestFrameBase64 { get; private set; }
    public bool IsStreaming => _pumpTask is { IsCompleted: false };
    public ElementHighlight? CurrentHighlight { get; private set; }

    public event Action<string>? FrameReceived;
    public event Action? HighlightChanged;

    public async Task AttachPageAsync(IPage? page, CancellationToken ct = default)
    {
        await StopAsync().ConfigureAwait(false);

        if (page is not InternalPage internalPage)
        {
            LatestFrameBase64 = null;
            CurrentHighlight = null;
            return;
        }

        _page = internalPage;
        _pumpCts = new CancellationTokenSource();

        // Stop the screencast before the page is disposed to avoid orphaned encoding
        _page.Close += OnPageClose;

        await _page.StartScreencastAsync(
            format: "jpeg",
            quality: 75,
            maxWidth: 1280,
            maxHeight: 720,
            everyNthFrame: 4,
            ct).ConfigureAwait(false);

        _pumpTask = PumpFramesAsync(_pumpCts.Token);
    }

    private void OnPageClose(object? sender, EventArgs e)
    {
        // Unsubscribe immediately and cancel the frame pump.
        // Full cleanup (awaiting _pumpTask, StopScreencastAsync) happens in
        // StopAsync/DisposeAsync to avoid a circular dependency where the pump
        // blocks on a CDP channel that is only completed after page disposal.
        if (sender is InternalPage page)
            page.Close -= OnPageClose;

        _pumpCts?.Cancel();
    }

    public async Task HighlightElementAsync(string? objectId, CancellationToken ct = default)
    {
        if (objectId is null || _page is null)
        {
            CurrentHighlight = null;
            HighlightChanged?.Invoke();
            return;
        }

        var result = await _page.GetBoxModelAsync(objectId, ct).ConfigureAwait(false);
        if (result is null)
        {
            CurrentHighlight = null;
            HighlightChanged?.Invoke();
            return;
        }

        var model = result.Model;
        var content = model.Content;

        // Content quad is 8 doubles: x1,y1, x2,y2, x3,y3, x4,y4
        // Compute axis-aligned bounding box
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        for (int i = 0; i < content.Length; i += 2)
        {
            var x = content[i];
            var y = content[i + 1];
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        CurrentHighlight = new ElementHighlight(minX, minY, maxX - minX, maxY - minY);
        HighlightChanged?.Invoke();
    }

    private async Task PumpFramesAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var frame in _page!.SubscribeScreencastFramesAsync(ct).ConfigureAwait(false))
            {
                LatestFrameBase64 = frame.Data;
                FrameReceived?.Invoke(frame.Data);

                try
                {
                    await _page.AckScreencastFrameAsync(frame.SessionId, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Ack failure is non-fatal; CDP will stop sending if not acked in time
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop
        }
    }

    private async Task StopAsync()
    {
        if (_pumpCts is not null)
        {
            await _pumpCts.CancelAsync().ConfigureAwait(false);

            if (_pumpTask is not null)
            {
                try { await _pumpTask.ConfigureAwait(false); }
                catch (OperationCanceledException) { }
            }

            _pumpCts.Dispose();
            _pumpCts = null;
        }

        if (_page is not null)
        {
            _page.Close -= OnPageClose;
            try { await _page.StopScreencastAsync().ConfigureAwait(false); }
            catch { /* Page may already be closed */ }
            _page = null;
        }

        _pumpTask = null;
        CurrentHighlight = null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
