using System.Text.Json;
using Motus.Abstractions;
using Motus.Tests.Transport;

namespace Motus.Tests.Input;

[TestClass]
public class KeyboardTests
{
    private FakeCdpSocket _socket = null!;
    private CdpTransport _transport = null!;
    private IMotusSession _session = null!;
    private Keyboard _keyboard = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _socket = new FakeCdpSocket();
        _transport = new CdpTransport(_socket);
        await _transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var registry = new CdpSessionRegistry(_transport);
        _session = registry.CreateSession("test-session");
        _keyboard = new Keyboard(_session, CancellationToken.None);
    }

    [TestCleanup]
    public async Task Cleanup() => await _transport.DisposeAsync();

    private void QueueOk(int count)
    {
        for (var i = 0; i < count; i++)
            _socket.QueueResponse("""{"id": 1, "sessionId": "test-session", "result": {}}""");
    }

    private JsonElement Sent(int index)
    {
        using var doc = JsonDocument.Parse(_socket.GetSentJson(index));
        return doc.RootElement.Clone();
    }

    private JsonElement Params(int index) => Sent(index).GetProperty("params");

    private static string? Str(JsonElement p, string name)
        => p.TryGetProperty(name, out var v) ? v.GetString() : null;

    private static int? Int(JsonElement p, string name)
        => p.TryGetProperty(name, out var v) ? v.GetInt32() : null;

    [TestMethod]
    public async Task DownAsync_Enter_SendsKeyDownWithCarriageReturnText()
    {
        QueueOk(1);
        await _keyboard.DownAsync("Enter");

        Assert.AreEqual(1, _socket.SentMessages.Count);
        Assert.AreEqual("Input.dispatchKeyEvent", Sent(0).GetProperty("method").GetString());
        var p = Params(0);
        Assert.AreEqual("keyDown", Str(p, "type"));
        Assert.AreEqual("Enter", Str(p, "key"));
        Assert.AreEqual("Enter", Str(p, "code"));
        Assert.AreEqual(13, Int(p, "windowsVirtualKeyCode"));
        Assert.AreEqual("\r", Str(p, "text"));
        Assert.AreEqual("\r", Str(p, "unmodifiedText"));
    }

    [TestMethod]
    public async Task DownAsync_PrintableChar_SendsOneKeyDownWithText()
    {
        QueueOk(1);
        await _keyboard.DownAsync("a");

        Assert.AreEqual(1, _socket.SentMessages.Count);
        var p = Params(0);
        Assert.AreEqual("keyDown", Str(p, "type"));
        Assert.AreEqual("a", Str(p, "key"));
        Assert.AreEqual("KeyA", Str(p, "code"));
        Assert.AreEqual(65, Int(p, "windowsVirtualKeyCode"));
        Assert.AreEqual("a", Str(p, "text"));
        Assert.IsFalse(_socket.GetSentJson(0).Contains("\"char\""));
    }

    [TestMethod]
    public async Task DownAsync_NumpadEnter_IsKeypadWithText()
    {
        QueueOk(1);
        await _keyboard.DownAsync("NumpadEnter");

        var p = Params(0);
        Assert.AreEqual("keyDown", Str(p, "type"));
        Assert.AreEqual("Enter", Str(p, "key"));
        Assert.AreEqual("NumpadEnter", Str(p, "code"));
        Assert.AreEqual(3, Int(p, "location"));
        Assert.IsTrue(p.GetProperty("isKeypad").GetBoolean());
        Assert.AreEqual("\r", Str(p, "text"));
    }

    [TestMethod]
    [DataRow("Tab", 9)]
    [DataRow("Backspace", 8)]
    [DataRow("Delete", 46)]
    [DataRow("Escape", 27)]
    [DataRow("ArrowLeft", 37)]
    [DataRow("Home", 36)]
    [DataRow("F5", 116)]
    public async Task DownAsync_NonTextKeys_SendRawKeyDownWithoutText(string key, int keyCode)
    {
        QueueOk(1);
        await _keyboard.DownAsync(key);

        var p = Params(0);
        Assert.AreEqual("rawKeyDown", Str(p, "type"));
        Assert.AreEqual(key, Str(p, "key"));
        Assert.AreEqual(keyCode, Int(p, "windowsVirtualKeyCode"));
        Assert.IsFalse(p.TryGetProperty("text", out _));
        Assert.IsFalse(p.TryGetProperty("unmodifiedText", out _));
    }

    [TestMethod]
    [DataRow(" ")]
    [DataRow("Space")]
    public async Task DownAsync_Space_SendsKeyDownWithSpaceText(string key)
    {
        QueueOk(1);
        await _keyboard.DownAsync(key);

        var p = Params(0);
        Assert.AreEqual("keyDown", Str(p, "type"));
        Assert.AreEqual(" ", Str(p, "key"));
        Assert.AreEqual("Space", Str(p, "code"));
        Assert.AreEqual(32, Int(p, "windowsVirtualKeyCode"));
        Assert.AreEqual(" ", Str(p, "text"));
    }

    /// <summary>
    /// A native key code must never reach the browser.
    /// </summary>
    /// <remarks>
    /// On macOS Chromium turns a nonzero nativeVirtualKeyCode into a real OS key event, and a
    /// Windows code read as a Mac one names a different key: Enter's 13 is the Mac code for W.
    /// That event was never released, so one press of Enter left the page receiving a stream of
    /// repeating keydowns for a key nobody pressed.
    /// </remarks>
    [TestMethod]
    public async Task KeyEvents_NeverCarryNativeVirtualKeyCode()
    {
        var names = KeyDefinitions.Keys.Keys.ToArray();
        QueueOk(names.Length * 2);

        foreach (var name in names)
        {
            await _keyboard.DownAsync(name);
            await _keyboard.UpAsync(name);
        }

        Assert.AreEqual(names.Length * 2, _socket.SentMessages.Count);
        for (var i = 0; i < _socket.SentMessages.Count; i++)
            Assert.IsFalse(Params(i).TryGetProperty("nativeVirtualKeyCode", out _), _socket.GetSentJson(i));
    }

    [TestMethod]
    [DataRow(".", "Period", 190)]
    [DataRow(",", "Comma", 188)]
    [DataRow("-", "Minus", 189)]
    [DataRow("$", "Digit4", 52)]
    [DataRow("%", "Digit5", 53)]
    [DataRow("!", "Digit1", 49)]
    [DataRow("(", "Digit9", 57)]
    [DataRow("'", "Quote", 222)]
    [DataRow("&", "Digit7", 55)]
    [DataRow("@", "Digit2", 50)]
    [DataRow("A", "KeyA", 65)]
    public void Resolve_Punctuation_UsesItsOwnKeyCode(string key, string code, int keyCode)
    {
        var def = KeyDefinitions.Resolve(key);

        Assert.AreEqual(key, def.Key);
        Assert.AreEqual(code, def.Code);
        Assert.AreEqual(keyCode, def.KeyCode);
        Assert.AreEqual(key, def.Text);
    }

    /// <summary>
    /// No character key may travel with a virtual key code the browser reads as navigation or
    /// editing. '.' used to go out as 46 (Delete) and '%' as 37 (ArrowLeft), so typing them
    /// deleted text or moved the caret instead of inserting anything.
    /// </summary>
    [TestMethod]
    public void Resolve_NoCharacterKey_UsesANavigationOrEditingKeyCode()
    {
        int[] reserved = [8, 9, 13, 27, 33, 34, 35, 36, 37, 38, 39, 40, 45, 46];

        foreach (var (name, def) in KeyDefinitions.Keys)
        {
            if (name.Length != 1 || name is "\n" or "\r")
                continue;

            CollectionAssert.DoesNotContain(reserved, def.KeyCode, $"'{name}' resolves to vk {def.KeyCode}");
        }
    }

    [TestMethod]
    public void Resolve_ExistingNames_StillResolve()
    {
        string[] names =
        [
            "Control", "Alt", "Shift", "Meta", "ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight",
            "Home", "End", "PageUp", "PageDown", "Enter", "Tab", "Backspace", "Delete", "Escape",
            " ", "Space", "Insert", "F1", "F12",
        ];

        foreach (var name in names)
            Assert.IsTrue(KeyDefinitions.TryResolve(name, out _), name);
    }

    [TestMethod]
    public async Task DownAsync_WhileShiftHeld_SendsShiftedKeyAndModifier()
    {
        QueueOk(2);
        await _keyboard.DownAsync("Shift");
        await _keyboard.DownAsync("a");

        var shift = Params(0);
        Assert.AreEqual("rawKeyDown", Str(shift, "type"));
        Assert.AreEqual(8, Int(shift, "modifiers"));

        var a = Params(1);
        Assert.AreEqual("keyDown", Str(a, "type"));
        Assert.AreEqual("A", Str(a, "key"));
        Assert.AreEqual("KeyA", Str(a, "code"));
        Assert.AreEqual("A", Str(a, "text"));
        Assert.AreEqual(8, Int(a, "modifiers"));
    }

    [TestMethod]
    [DataRow("Control", 2)]
    [DataRow("Alt", 1)]
    [DataRow("Meta", 4)]
    public async Task DownAsync_WhileShortcutModifierHeld_SendsNoText(string modifier, int bit)
    {
        QueueOk(2);
        await _keyboard.DownAsync(modifier);
        await _keyboard.DownAsync("a");

        var a = Params(1);
        Assert.AreEqual("rawKeyDown", Str(a, "type"));
        Assert.AreEqual("a", Str(a, "key"));
        Assert.AreEqual(bit, Int(a, "modifiers"));
        Assert.IsFalse(a.TryGetProperty("text", out _));
    }

    [TestMethod]
    public async Task DownAsync_ControlEnter_DoesNotCarryText()
    {
        QueueOk(2);
        await _keyboard.DownAsync("Control");
        await _keyboard.DownAsync("Enter");

        var enter = Params(1);
        Assert.AreEqual("rawKeyDown", Str(enter, "type"));
        Assert.IsFalse(enter.TryGetProperty("text", out _));
    }

    [TestMethod]
    public async Task ModifierKeyDown_CarriesItsOwnBit_AndKeyUpClearsIt()
    {
        QueueOk(4);
        await _keyboard.DownAsync("Control");
        await _keyboard.DownAsync("Shift");
        await _keyboard.UpAsync("Shift");
        await _keyboard.UpAsync("Control");

        Assert.AreEqual(2, Int(Params(0), "modifiers"));
        Assert.AreEqual(10, Int(Params(1), "modifiers"));
        Assert.AreEqual(2, Int(Params(2), "modifiers"));
        Assert.AreEqual(0, Int(Params(3), "modifiers"));
    }

    [TestMethod]
    [DataRow("ShiftRight", "Shift", 16, 8)]
    [DataRow("ControlRight", "Control", 17, 2)]
    [DataRow("AltRight", "Alt", 18, 1)]
    [DataRow("MetaRight", "Meta", 92, 4)]
    public async Task RightSideModifiers_HaveLocation2_AndSetTheirBit(string name, string key, int keyCode, int bit)
    {
        QueueOk(1);
        await _keyboard.DownAsync(name);

        var p = Params(0);
        Assert.AreEqual(key, Str(p, "key"));
        Assert.AreEqual(name, Str(p, "code"));
        Assert.AreEqual(keyCode, Int(p, "windowsVirtualKeyCode"));
        Assert.AreEqual(2, Int(p, "location"));
        Assert.AreEqual(bit, Int(p, "modifiers"));
    }

    [TestMethod]
    public async Task DownAsync_HeldKey_ReportsAutoRepeat()
    {
        QueueOk(3);
        await _keyboard.DownAsync("a");
        await _keyboard.DownAsync("a");
        await _keyboard.UpAsync("a");

        Assert.IsFalse(Params(0).GetProperty("autoRepeat").GetBoolean());
        Assert.IsTrue(Params(1).GetProperty("autoRepeat").GetBoolean());
    }

    [TestMethod]
    public async Task UpAsync_HasNoText()
    {
        QueueOk(1);
        await _keyboard.UpAsync("Enter");

        var p = Params(0);
        Assert.AreEqual("keyUp", Str(p, "type"));
        Assert.AreEqual("Enter", Str(p, "key"));
        Assert.AreEqual(13, Int(p, "windowsVirtualKeyCode"));
        Assert.IsFalse(p.TryGetProperty("text", out _));
        Assert.IsFalse(p.TryGetProperty("unmodifiedText", out _));
    }

    [TestMethod]
    public async Task PressAsync_SendsDownAndUp()
    {
        QueueOk(2);
        await _keyboard.PressAsync("Enter");

        Assert.AreEqual(2, _socket.SentMessages.Count);
        Assert.AreEqual("keyDown", Str(Params(0), "type"));
        Assert.AreEqual("keyUp", Str(Params(1), "type"));
    }

    [TestMethod]
    public async Task PressAsync_CompoundKey_SendsModifiersAndKey()
    {
        // Control down, a down, a up, Control up
        QueueOk(4);
        await _keyboard.PressAsync("Control+a");

        Assert.AreEqual(4, _socket.SentMessages.Count);

        var controlDown = Params(0);
        Assert.AreEqual("rawKeyDown", Str(controlDown, "type"));
        Assert.AreEqual("Control", Str(controlDown, "key"));
        Assert.AreEqual(2, Int(controlDown, "modifiers"));

        var aDown = Params(1);
        Assert.AreEqual("rawKeyDown", Str(aDown, "type"));
        Assert.AreEqual("a", Str(aDown, "key"));
        Assert.AreEqual(2, Int(aDown, "modifiers"));
        Assert.IsFalse(aDown.TryGetProperty("text", out _));

        var aUp = Params(2);
        Assert.AreEqual("keyUp", Str(aUp, "type"));
        Assert.AreEqual(2, Int(aUp, "modifiers"));

        var controlUp = Params(3);
        Assert.AreEqual("keyUp", Str(controlUp, "type"));
        Assert.AreEqual("Control", Str(controlUp, "key"));
        Assert.AreEqual(0, Int(controlUp, "modifiers"));
    }

    [TestMethod]
    public async Task PressAsync_PlusOnItsOwn_PressesThePlusKey()
    {
        QueueOk(2);
        await _keyboard.PressAsync("+");

        Assert.AreEqual(2, _socket.SentMessages.Count);
        var p = Params(0);
        Assert.AreEqual("+", Str(p, "key"));
        Assert.AreEqual("Equal", Str(p, "code"));
        Assert.AreEqual(187, Int(p, "windowsVirtualKeyCode"));
        Assert.AreEqual("+", Str(p, "text"));
    }

    [TestMethod]
    public async Task PressAsync_ShiftPlus_PressesShiftThenPlus()
    {
        QueueOk(4);
        await _keyboard.PressAsync("Shift++");

        Assert.AreEqual(4, _socket.SentMessages.Count);
        Assert.AreEqual("Shift", Str(Params(0), "key"));
        Assert.AreEqual("+", Str(Params(1), "key"));
        Assert.AreEqual(8, Int(Params(1), "modifiers"));
        Assert.AreEqual("+", Str(Params(2), "key"));
        Assert.AreEqual("Shift", Str(Params(3), "key"));
    }

    [TestMethod]
    [DataRow("+", new[] { "+" })]
    [DataRow("Shift++", new[] { "Shift", "+" })]
    [DataRow("Control+Shift+a", new[] { "Control", "Shift", "a" })]
    [DataRow("Enter", new[] { "Enter" })]
    public void SplitChord_SeparatesOnPlusThatFollowsAKey(string chord, string[] expected)
        => CollectionAssert.AreEqual(expected, Keyboard.SplitChord(chord));

    [TestMethod]
    [DataRow("Esc")]
    [DataRow("Unidentified")]
    [DataRow("Dead")]
    [DataRow("")]
    public async Task PressAsync_UnknownKey_Throws(string key)
    {
        var ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => _keyboard.PressAsync(key));
        Assert.AreEqual($"Unknown key: '{key}'", ex.Message);
        Assert.AreEqual(0, _socket.SentMessages.Count);
    }

    [TestMethod]
    public async Task PressAsync_UnknownKeyInChord_SendsNothing()
    {
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => _keyboard.PressAsync("Control+Esc"));
        Assert.AreEqual(0, _socket.SentMessages.Count);
    }

    [TestMethod]
    public async Task DownAsync_UnknownKey_Throws()
        => await Assert.ThrowsExceptionAsync<ArgumentException>(() => _keyboard.DownAsync("Return"));

    [TestMethod]
    public async Task TypeAsync_IteratesCharacters()
    {
        // "hi" = 2 chars, each press = keyDown + keyUp
        QueueOk(4);
        await _keyboard.TypeAsync("hi");

        Assert.AreEqual(4, _socket.SentMessages.Count);
        Assert.AreEqual("h", Str(Params(0), "text"));
        Assert.AreEqual("i", Str(Params(2), "text"));
    }

    [TestMethod]
    [DataRow("a\nb")]
    [DataRow("a\rb")]
    public async Task TypeAsync_NewlineMapsToEnter(string text)
    {
        QueueOk(6);
        await _keyboard.TypeAsync(text);

        Assert.AreEqual(6, _socket.SentMessages.Count);
        var enter = Params(2);
        Assert.AreEqual("keyDown", Str(enter, "type"));
        Assert.AreEqual("Enter", Str(enter, "key"));
        Assert.AreEqual("\r", Str(enter, "text"));
    }

    [TestMethod]
    public async Task TypeAsync_Punctuation_SendsEachKeyWithItsText()
    {
        const string text = "bob.smith+1@example.com $5 (50%)!";
        QueueOk(text.Length * 2);
        await _keyboard.TypeAsync(text);

        Assert.AreEqual(text.Length * 2, _socket.SentMessages.Count);
        var typed = string.Concat(Enumerable.Range(0, text.Length).Select(i => Str(Params(i * 2), "text")));
        Assert.AreEqual(text, typed);
    }

    [TestMethod]
    [DataRow("é")]
    [DataRow("\U0001F600")]
    [DataRow("中")]
    [DataRow("\t")]
    public async Task TypeAsync_CharacterOffTheLayout_UsesInsertText(string character)
    {
        QueueOk(1);
        await _keyboard.TypeAsync(character);

        Assert.AreEqual(1, _socket.SentMessages.Count);
        var sent = Sent(0);
        Assert.AreEqual("Input.insertText", sent.GetProperty("method").GetString());
        Assert.AreEqual(character, sent.GetProperty("params").GetProperty("text").GetString());
    }

    [TestMethod]
    public async Task TypeAsync_MixedText_KeepsSurrogatePairsWhole()
    {
        // a press (2), emoji insertText (1), b press (2)
        QueueOk(5);
        await _keyboard.TypeAsync("a\U0001F600b");

        Assert.AreEqual(5, _socket.SentMessages.Count);
        Assert.AreEqual("a", Str(Params(0), "text"));
        Assert.AreEqual("Input.insertText", Sent(2).GetProperty("method").GetString());
        Assert.AreEqual("\U0001F600", Str(Params(2), "text"));
        Assert.AreEqual("b", Str(Params(3), "text"));
    }

    /// <summary>
    /// WebDriver BiDi has no insertText, so over BiDi a character off the US layout is pressed as
    /// itself, which a BiDi key action accepts, rather than failing as an unsupported command.
    /// </summary>
    [TestMethod]
    public async Task TypeAsync_CharacterOffTheLayout_OverBiDi_PressesTheCharacter()
    {
        var socket = new FakeCdpSocket();
        await using var transport = new BiDiTransport(socket);
        await transport.ConnectAsync(new Uri("ws://localhost:1234"), CancellationToken.None);
        var keyboard = new Keyboard(new BiDiSession(transport, "ctx-1"), CancellationToken.None);

        socket.QueueResponse("""{"type":"success","id":1,"result":{}}""");
        socket.QueueResponse("""{"type":"success","id":2,"result":{}}""");
        await keyboard.TypeAsync("é");

        Assert.AreEqual(2, socket.SentMessages.Count);
        string[] expected = ["keyDown", "keyUp"];
        for (var i = 0; i < expected.Length; i++)
        {
            using var doc = JsonDocument.Parse(socket.GetSentJson(i));
            Assert.AreEqual("input.performActions", doc.RootElement.GetProperty("method").GetString());
            var action = doc.RootElement.GetProperty("params").GetProperty("actions")[0].GetProperty("actions")[0];
            Assert.AreEqual(expected[i], action.GetProperty("type").GetString());
            Assert.AreEqual("é", action.GetProperty("value").GetString());
        }
    }

    [TestMethod]
    public async Task InsertTextAsync_SendsInsertText()
    {
        QueueOk(1);
        await _keyboard.InsertTextAsync("hello world");

        var sent = _socket.GetSentJson(0);
        Assert.IsTrue(sent.Contains("Input.insertText"));
        Assert.IsTrue(sent.Contains("hello world"));
    }
}
