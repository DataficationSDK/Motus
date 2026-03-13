using Motus.Abstractions;

namespace Motus;

internal sealed class Keyboard : IKeyboard
{
    private readonly CdpSession _session;
    private readonly CancellationToken _ct;

    internal Keyboard(CdpSession session, CancellationToken ct)
    {
        _session = session;
        _ct = ct;
    }

    public async Task DownAsync(string key)
    {
        var def = KeyDefinitions.Resolve(key);

        await _session.SendAsync(
            "Input.dispatchKeyEvent",
            new InputDispatchKeyEventParams(
                Type: "rawKeyDown",
                Code: def.Code,
                Key: def.Key,
                WindowsVirtualKeyCode: def.KeyCode,
                NativeVirtualKeyCode: def.KeyCode,
                Location: def.Location),
            CdpJsonContext.Default.InputDispatchKeyEventParams,
            CdpJsonContext.Default.InputDispatchKeyEventResult,
            _ct);

        // For printable characters, also send a char event
        if (def.Key.Length == 1 && !KeyDefinitions.IsModifier(key))
        {
            await _session.SendAsync(
                "Input.dispatchKeyEvent",
                new InputDispatchKeyEventParams(
                    Type: "char",
                    Code: def.Code,
                    Key: def.Key,
                    Text: def.Key,
                    UnmodifiedText: def.Key,
                    WindowsVirtualKeyCode: def.KeyCode,
                    NativeVirtualKeyCode: def.KeyCode,
                    Location: def.Location),
                CdpJsonContext.Default.InputDispatchKeyEventParams,
                CdpJsonContext.Default.InputDispatchKeyEventResult,
                _ct);
        }
    }

    public async Task UpAsync(string key)
    {
        var def = KeyDefinitions.Resolve(key);

        await _session.SendAsync(
            "Input.dispatchKeyEvent",
            new InputDispatchKeyEventParams(
                Type: "keyUp",
                Code: def.Code,
                Key: def.Key,
                WindowsVirtualKeyCode: def.KeyCode,
                NativeVirtualKeyCode: def.KeyCode,
                Location: def.Location),
            CdpJsonContext.Default.InputDispatchKeyEventParams,
            CdpJsonContext.Default.InputDispatchKeyEventResult,
            _ct);
    }

    public async Task PressAsync(string key, KeyboardPressOptions? options = null)
    {
        if (key.Contains('+'))
        {
            var parts = key.Split('+');
            var modifiers = parts[..^1];
            var finalKey = parts[^1];

            foreach (var mod in modifiers)
                await DownAsync(mod);

            await DownAsync(finalKey);

            if (options?.Delay is > 0)
                await Task.Delay(options.Delay.Value, _ct);

            await UpAsync(finalKey);

            foreach (var mod in modifiers.AsEnumerable().Reverse())
                await UpAsync(mod);
        }
        else
        {
            await DownAsync(key);

            if (options?.Delay is > 0)
                await Task.Delay(options.Delay.Value, _ct);

            await UpAsync(key);
        }
    }

    public async Task TypeAsync(string text, KeyboardTypeOptions? options = null)
    {
        foreach (var c in text)
        {
            await PressAsync(c.ToString());

            if (options?.Delay is > 0)
                await Task.Delay(options.Delay.Value, _ct);
        }
    }

    public async Task InsertTextAsync(string text)
    {
        await _session.SendAsync(
            "Input.insertText",
            new InputInsertTextParams(text),
            CdpJsonContext.Default.InputInsertTextParams,
            CdpJsonContext.Default.InputInsertTextResult,
            _ct);
    }
}
