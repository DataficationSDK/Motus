using Motus.Abstractions;

namespace Motus;

internal sealed class Touchscreen : ITouchscreen
{
    private readonly CdpSession _session;
    private readonly CancellationToken _ct;

    internal Touchscreen(CdpSession session, CancellationToken ct)
    {
        _session = session;
        _ct = ct;
    }

    public async Task TapAsync(double x, double y)
    {
        await _session.SendAsync(
            "Input.dispatchTouchEvent",
            new InputDispatchTouchEventParams(
                Type: "touchStart",
                TouchPoints: [new InputTouchPoint(X: x, Y: y)]),
            CdpJsonContext.Default.InputDispatchTouchEventParams,
            CdpJsonContext.Default.InputDispatchTouchEventResult,
            _ct);

        await _session.SendAsync(
            "Input.dispatchTouchEvent",
            new InputDispatchTouchEventParams(
                Type: "touchEnd",
                TouchPoints: []),
            CdpJsonContext.Default.InputDispatchTouchEventParams,
            CdpJsonContext.Default.InputDispatchTouchEventResult,
            _ct);
    }
}
