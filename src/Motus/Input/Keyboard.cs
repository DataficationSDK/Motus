using System.Text;
using Motus.Abstractions;

namespace Motus;

internal sealed class Keyboard : IKeyboard
{
    private readonly IMotusSession _session;
    private readonly CancellationToken _ct;

    // Modifier bits currently held, sent with every key event so the page sees Shift+a as "A"
    // and Control+a as a shortcut rather than text.
    private int _modifiers;

    // Codes currently held, so a second DownAsync on a held key reports itself as a repeat.
    private readonly HashSet<string> _pressedCodes = new(StringComparer.Ordinal);

    internal Keyboard(IMotusSession session, CancellationToken ct)
    {
        _session = session;
        _ct = ct;
    }

    public async Task DownAsync(string key)
    {
        var def = ResolveForCurrentModifiers(key);
        _modifiers |= KeyDefinitions.ModifierBit(def);
        var autoRepeat = !_pressedCodes.Add(def.Code);

        // A held Control, Alt or Meta turns the key into a shortcut, which produces no text.
        var text = (_modifiers & ~KeyDefinitions.ShiftBit) != 0 ? string.Empty : def.Text;

        // A key that produces text goes out as a single keyDown carrying that text; the browser
        // derives keypress and input from it, which is what lets Enter activate a button or
        // submit a form. A key without text goes out as rawKeyDown. No native key code is sent:
        // on macOS the browser builds a real OS event from it, and a Windows code read as a Mac
        // one leaves a phantom key held down.
        await _session.SendAsync(
            "Input.dispatchKeyEvent",
            new InputDispatchKeyEventParams(
                Type: text.Length > 0 ? "keyDown" : "rawKeyDown",
                Modifiers: _modifiers,
                Text: text.Length > 0 ? text : null,
                UnmodifiedText: text.Length > 0 ? text : null,
                Code: def.Code,
                Key: def.Key,
                WindowsVirtualKeyCode: def.KeyCode,
                AutoRepeat: autoRepeat,
                IsKeypad: def.Location == 3 ? true : null,
                Location: def.Location),
            CdpJsonContext.Default.InputDispatchKeyEventParams,
            CdpJsonContext.Default.InputDispatchKeyEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task UpAsync(string key)
    {
        var def = ResolveForCurrentModifiers(key);
        _modifiers &= ~KeyDefinitions.ModifierBit(def);
        _pressedCodes.Remove(def.Code);

        await _session.SendAsync(
            "Input.dispatchKeyEvent",
            new InputDispatchKeyEventParams(
                Type: "keyUp",
                Modifiers: _modifiers,
                Code: def.Code,
                Key: def.Key,
                WindowsVirtualKeyCode: def.KeyCode,
                IsKeypad: def.Location == 3 ? true : null,
                Location: def.Location),
            CdpJsonContext.Default.InputDispatchKeyEventParams,
            CdpJsonContext.Default.InputDispatchKeyEventResult,
            _ct).ConfigureAwait(false);
    }

    public async Task PressAsync(string key, KeyboardPressOptions? options = null)
    {
        var keys = SplitChord(key);

        // Every part is checked before any goes down, so a typo cannot leave a modifier held.
        foreach (var part in keys)
            KeyDefinitions.Resolve(part);

        foreach (var part in keys)
            await DownAsync(part).ConfigureAwait(false);

        if (options?.Delay is > 0)
            await Task.Delay(options.Delay.Value, _ct).ConfigureAwait(false);

        for (var i = keys.Count - 1; i >= 0; i--)
            await UpAsync(keys[i]).ConfigureAwait(false);
    }

    public async Task TypeAsync(string text, KeyboardTypeOptions? options = null)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var character = rune.ToString();

            if (KeyDefinitions.TryResolve(character, out _))
                await PressAsync(character).ConfigureAwait(false);
            else if (_session is BiDiSession)
                await PressUnmappedOverBiDiAsync(character).ConfigureAwait(false);
            else
                await InsertTextAsync(character).ConfigureAwait(false);

            if (options?.Delay is > 0)
                await Task.Delay(options.Delay.Value, _ct).ConfigureAwait(false);
        }
    }

    public async Task InsertTextAsync(string text)
    {
        await _session.SendAsync(
            "Input.insertText",
            new InputInsertTextParams(text),
            CdpJsonContext.Default.InputInsertTextParams,
            CdpJsonContext.Default.InputInsertTextResult,
            _ct).ConfigureAwait(false);
    }

    private KeyDefinitions.KeyDef ResolveForCurrentModifiers(string key)
    {
        var def = KeyDefinitions.Resolve(key);
        return (_modifiers & KeyDefinitions.ShiftBit) != 0 && def.Shifted is not null ? def.Shifted : def;
    }

    // WebDriver BiDi has no insertText, but its key actions accept any single character as a key
    // value, so a character missing from the US layout is pressed as itself there.
    private async Task PressUnmappedOverBiDiAsync(string character)
    {
        await SendBareKeyEventAsync("keyDown", character).ConfigureAwait(false);
        await SendBareKeyEventAsync("keyUp", character).ConfigureAwait(false);
    }

    private Task SendBareKeyEventAsync(string type, string key)
        => _session.SendAsync(
            "Input.dispatchKeyEvent",
            new InputDispatchKeyEventParams(Type: type, Key: key),
            CdpJsonContext.Default.InputDispatchKeyEventParams,
            CdpJsonContext.Default.InputDispatchKeyEventResult,
            _ct);

    /// <summary>
    /// Splits a chord such as "Control+Shift+a" into its keys. A '+' only separates keys when it
    /// follows a key name, so "+" on its own and the last key of "Shift++" are the plus key.
    /// </summary>
    internal static List<string> SplitChord(string chord)
    {
        var keys = new List<string>();
        var building = new StringBuilder();

        foreach (var c in chord)
        {
            if (c == '+' && building.Length > 0)
            {
                keys.Add(building.ToString());
                building.Clear();
            }
            else
            {
                building.Append(c);
            }
        }

        keys.Add(building.ToString());
        return keys;
    }
}
