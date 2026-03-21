using Motus.Abstractions;

namespace Motus;

internal sealed class Mouse : IMouse
{
    private readonly IMotusSession _session;
    private readonly CancellationToken _ct;
    private double _x;
    private double _y;

    internal Mouse(IMotusSession session, CancellationToken ct)
    {
        _session = session;
        _ct = ct;
    }

    public async Task MoveAsync(double x, double y, MouseMoveOptions? options = null)
    {
        var steps = options?.Steps ?? 1;
        if (steps < 1) steps = 1;

        var fromX = _x;
        var fromY = _y;

        for (var i = 1; i <= steps; i++)
        {
            var currentX = fromX + (x - fromX) * i / steps;
            var currentY = fromY + (y - fromY) * i / steps;

            await _session.SendAsync(
                "Input.dispatchMouseEvent",
                new InputDispatchMouseEventParams(
                    Type: "mouseMoved",
                    X: currentX,
                    Y: currentY),
                CdpJsonContext.Default.InputDispatchMouseEventParams,
                CdpJsonContext.Default.InputDispatchMouseEventResult,
                _ct).ConfigureAwait(false);
        }

        _x = x;
        _y = y;
    }

    public async Task DownAsync(MouseButtonOptions? options = null)
    {
        var button = MapButton(options?.Button ?? MouseButton.Left);
        var clickCount = options?.ClickCount ?? 1;

        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(
                Type: "mousePressed",
                X: _x,
                Y: _y,
                Button: button,
                ClickCount: clickCount),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task UpAsync(MouseButtonOptions? options = null)
    {
        var button = MapButton(options?.Button ?? MouseButton.Left);
        var clickCount = options?.ClickCount ?? 1;

        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(
                Type: "mouseReleased",
                X: _x,
                Y: _y,
                Button: button,
                ClickCount: clickCount),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task ClickAsync(double x, double y, MouseButtonOptions? options = null)
    {
        await MoveAsync(x, y).ConfigureAwait(false);
        await DownAsync(options).ConfigureAwait(false);

        if (options?.Delay is > 0)
            await Task.Delay(options.Delay.Value, _ct).ConfigureAwait(false);

        await UpAsync(options).ConfigureAwait(false);
    }

    public async Task DblClickAsync(double x, double y, MouseButtonOptions? options = null)
    {
        await MoveAsync(x, y).ConfigureAwait(false);

        var button = MapButton(options?.Button ?? MouseButton.Left);

        // First click
        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mousePressed", X: _x, Y: _y, Button: button, ClickCount: 1),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);

        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mouseReleased", X: _x, Y: _y, Button: button, ClickCount: 1),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);

        // Second click
        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mousePressed", X: _x, Y: _y, Button: button, ClickCount: 2),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);

        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mouseReleased", X: _x, Y: _y, Button: button, ClickCount: 2),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task WheelAsync(double deltaX, double deltaY)
    {
        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(
                Type: "mouseWheel",
                X: _x,
                Y: _y,
                DeltaX: deltaX,
                DeltaY: deltaY),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);
    }

    private static string MapButton(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "left"
    };
}
