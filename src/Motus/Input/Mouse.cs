using Motus.Abstractions;

namespace Motus;

internal sealed class Mouse : IMouse
{
    private readonly IMotusSession _session;
    private readonly CancellationToken _ct;
    private readonly bool _natural;
    private double _x;
    private double _y;

    internal Mouse(IMotusSession session, CancellationToken ct, bool naturalMotion = false)
    {
        _session = session;
        _ct = ct;
        _natural = naturalMotion;
    }

    public async Task MoveAsync(double x, double y, MouseMoveOptions? options = null)
    {
        if (_natural)
        {
            await MoveNaturalAsync(x, y, options).ConfigureAwait(false);
            return;
        }

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
                    Y: currentY,
                    Modifiers: MapModifiers(options?.Modifiers)),
                CdpJsonContext.Default.InputDispatchMouseEventParams,
                CdpJsonContext.Default.InputDispatchMouseEventResult,
                _ct).ConfigureAwait(false);
        }

        _x = x;
        _y = y;
    }

    // Moves along a curved, eased, time-spaced path so motion looks human in recordings and the
    // page receives a realistic event stream. The step count is a deterministic function of
    // distance (only the curve shape and jitter are randomized), and the final event always lands
    // exactly on the target. MouseMoveOptions.Steps is ignored here; the path computes its own.
    private async Task MoveNaturalAsync(double x, double y, MouseMoveOptions? options)
    {
        var fromX = _x;
        var fromY = _y;
        var dx = x - fromX;
        var dy = y - fromY;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var modifiers = MapModifiers(options?.Modifiers);

        if (distance < 4)
        {
            await SendMoveAsync(x, y, modifiers).ConfigureAwait(false);
            _x = x;
            _y = y;
            return;
        }

        var steps = Math.Clamp((int)(distance / 8), 12, 48);
        var rng = Random.Shared;

        // Two control points along the path, nudged perpendicular to it for a gentle bow.
        var nx = -dy / distance;
        var ny = dx / distance;
        var bow = Math.Min(distance * 0.18, 80) * (rng.NextDouble() * 2 - 1);
        var c1x = fromX + dx * 0.33 + nx * bow;
        var c1y = fromY + dy * 0.33 + ny * bow;
        var c2x = fromX + dx * 0.66 + nx * bow * 0.6;
        var c2y = fromY + dy * 0.66 + ny * bow * 0.6;

        for (var i = 1; i <= steps; i++)
        {
            var t = Smoothstep((double)i / steps);
            double px, py;
            if (i == steps)
            {
                px = x;          // land exactly on the target
                py = y;
            }
            else
            {
                Bezier(t, fromX, fromY, c1x, c1y, c2x, c2y, x, y, out px, out py);
                var jitter = Math.Min(distance * 0.01, 1.2);
                px += (rng.NextDouble() * 2 - 1) * jitter;
                py += (rng.NextDouble() * 2 - 1) * jitter;
            }

            await SendMoveAsync(px, py, modifiers).ConfigureAwait(false);

            if (i < steps)
                await Task.Delay(8 + rng.Next(0, 7), _ct).ConfigureAwait(false);
        }

        _x = x;
        _y = y;
    }

    private Task SendMoveAsync(double x, double y, int? modifiers) =>
        _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mouseMoved", X: x, Y: y, Modifiers: modifiers),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct);

    // Smooth ease-in-out so the pointer accelerates off the start and decelerates into the target.
    private static double Smoothstep(double t) => t * t * (3 - 2 * t);

    private static void Bezier(
        double t, double p0x, double p0y, double p1x, double p1y,
        double p2x, double p2y, double p3x, double p3y, out double x, out double y)
    {
        var u = 1 - t;
        var a = u * u * u;
        var b = 3 * u * u * t;
        var c = 3 * u * t * t;
        var d = t * t * t;
        x = a * p0x + b * p1x + c * p2x + d * p3x;
        y = a * p0y + b * p1y + c * p2y + d * p3y;
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
                Modifiers: MapModifiers(options?.Modifiers),
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
                Modifiers: MapModifiers(options?.Modifiers),
                Button: button,
                ClickCount: clickCount),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task ClickAsync(double x, double y, MouseButtonOptions? options = null)
    {
        await MoveAsync(x, y, MoveOptionsFrom(options)).ConfigureAwait(false);
        await DownAsync(options).ConfigureAwait(false);

        if (options?.Delay is > 0)
            await Task.Delay(options.Delay.Value, _ct).ConfigureAwait(false);

        await UpAsync(options).ConfigureAwait(false);
    }

    public async Task DblClickAsync(double x, double y, MouseButtonOptions? options = null)
    {
        await MoveAsync(x, y, MoveOptionsFrom(options)).ConfigureAwait(false);

        var button = MapButton(options?.Button ?? MouseButton.Left);
        var modifiers = MapModifiers(options?.Modifiers);

        // First click
        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mousePressed", X: _x, Y: _y, Modifiers: modifiers, Button: button, ClickCount: 1),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);

        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mouseReleased", X: _x, Y: _y, Modifiers: modifiers, Button: button, ClickCount: 1),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);

        // Second click
        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mousePressed", X: _x, Y: _y, Modifiers: modifiers, Button: button, ClickCount: 2),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);

        await _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(Type: "mouseReleased", X: _x, Y: _y, Modifiers: modifiers, Button: button, ClickCount: 2),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task WheelAsync(double deltaX, double deltaY)
    {
        // With natural motion, glide a sizable scroll across several eased steps so it does not jump
        // in a single hop. Small nudges and the non-natural path stay a single event.
        if (_natural && (Math.Abs(deltaX) > 50 || Math.Abs(deltaY) > 50))
        {
            await WheelNaturalAsync(deltaX, deltaY).ConfigureAwait(false);
            return;
        }

        await SendWheelAsync(deltaX, deltaY).ConfigureAwait(false);
    }

    // Partitions a wheel delta into ease-in-out increments spaced by short delays so the scroll
    // accelerates and settles like a human flick. The increments sum exactly to the requested delta,
    // so the final scroll position matches the single-event path.
    private async Task WheelNaturalAsync(double deltaX, double deltaY)
    {
        var distance = Math.Max(Math.Abs(deltaX), Math.Abs(deltaY));
        var steps = Math.Clamp((int)(distance / 40), 8, 30);
        var rng = Random.Shared;

        double sentX = 0, sentY = 0;
        for (var i = 1; i <= steps; i++)
        {
            var eased = Smoothstep((double)i / steps);
            // The last step takes whatever remains so rounding never drifts off the target total.
            var stepX = i == steps ? deltaX - sentX : deltaX * eased - sentX;
            var stepY = i == steps ? deltaY - sentY : deltaY * eased - sentY;

            await SendWheelAsync(stepX, stepY).ConfigureAwait(false);
            sentX += stepX;
            sentY += stepY;

            if (i < steps)
                await Task.Delay(8 + rng.Next(0, 8), _ct).ConfigureAwait(false);
        }
    }

    private Task SendWheelAsync(double deltaX, double deltaY) =>
        _session.SendAsync(
            "Input.dispatchMouseEvent",
            new InputDispatchMouseEventParams(
                Type: "mouseWheel",
                X: _x,
                Y: _y,
                DeltaX: deltaX,
                DeltaY: deltaY),
            CdpJsonContext.Default.InputDispatchMouseEventParams,
            CdpJsonContext.Default.InputDispatchMouseEventResult,
            _ct);

    private static string MapButton(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "left"
    };

    // KeyModifier flag values match the CDP Input domain modifier bits, so the
    // flags pass through as-is; None is omitted entirely.
    private static int? MapModifiers(KeyModifier? modifiers)
        => modifiers is null or KeyModifier.None ? null : (int)modifiers;

    private static MouseMoveOptions? MoveOptionsFrom(MouseButtonOptions? options)
        => options is { Modifiers: not KeyModifier.None }
            ? new MouseMoveOptions(Modifiers: options.Modifiers)
            : null;
}
