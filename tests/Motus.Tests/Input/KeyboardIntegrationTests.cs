using Motus.Abstractions;

namespace Motus.Tests.Input;

/// <summary>
/// Drives the real keyboard against a page and checks what the page saw.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class KeyboardIntegrationTests
{
    private IBrowser? _browser;
    private IPage _page = null!;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            _browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
        }

        _page = await _browser!.NewPageAsync();
        await _page.GotoAsync("data:text/html,<title>keyboard</title>");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
    }

    private async Task LoadAsync(string body)
    {
        await _page.SetContentAsync($$"""
            <html><body>
            {{body}}
            <script>
                window.keydowns = [];
                window.clicks = 0;
                window.submits = 0;
                document.addEventListener('keydown', e => window.keydowns.push(e.key + '/' + e.code), true);
                document.addEventListener('click', () => window.clicks++, true);
                document.addEventListener('submit', e => { e.preventDefault(); window.submits++; }, true);
            </script>
            </body></html>
            """);
    }

    private Task FocusAsync(string selector)
        => _page.EvaluateAsync<bool>($"(() => {{ document.querySelector('{selector}').focus(); return true; }})()");

    private Task<string> ValueAsync(string selector)
        => _page.EvaluateAsync<string>($"document.querySelector('{selector}').value");

    private Task<int> CountAsync(string name)
        => _page.EvaluateAsync<int>($"window.{name}");

    /// <summary>
    /// One press of Enter has to reach the page as exactly one keydown.
    /// </summary>
    /// <remarks>
    /// On macOS each key event used to carry its Windows virtual key code as the native code, so
    /// Chromium built a real OS key event for a different key (13 is the Mac code for W) and never
    /// released it. The page then saw a stream of repeating "Unidentified" keydowns, hundreds a
    /// second, until the browser went away. The pause gives such a stream time to show.
    /// </remarks>
    [TestMethod]
    public async Task PressAsync_Enter_FiresExactlyOneKeydown()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Enter");
        await _page.Keyboard.PressAsync("a");
        await Task.Delay(500);

        var keydowns = await _page.EvaluateAsync<string>("window.keydowns.join(',')");
        Assert.AreEqual("Enter/Enter,a/KeyA", keydowns);
    }

    [TestMethod]
    public async Task PressAsync_Enter_OnFocusedButton_ClicksItOnce()
    {
        await LoadAsync("<button id='b' type='button'>Cancel</button>");
        await FocusAsync("#b");

        await _page.Keyboard.PressAsync("Enter");

        Assert.AreEqual(1, await CountAsync("clicks"));
    }

    [TestMethod]
    public async Task PressAsync_NumpadEnter_OnFocusedButton_ClicksIt()
    {
        await LoadAsync("<button id='b' type='button'>OK</button>");
        await FocusAsync("#b");

        await _page.Keyboard.PressAsync("NumpadEnter");

        Assert.AreEqual(1, await CountAsync("clicks"));
    }

    [TestMethod]
    public async Task PressAsync_Enter_InTextInput_SubmitsForm()
    {
        await LoadAsync("<form><input id='f' name='q' value='x'><button type='submit'>Go</button></form>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Enter");

        Assert.AreEqual(1, await CountAsync("submits"));
    }

    [TestMethod]
    public async Task PressAsync_Space_TogglesCheckbox()
    {
        await LoadAsync("<input id='c' type='checkbox'>");
        await FocusAsync("#c");

        await _page.Keyboard.PressAsync("Space");

        Assert.IsTrue(await _page.EvaluateAsync<bool>("document.querySelector('#c').checked"));
    }

    [TestMethod]
    public async Task TypeAsync_Punctuation_TypesVerbatim()
    {
        const string text = "bob.smith+1@example.com $5 (50%)! [a]{b}<c>|~`^&*_=-;:'\",./?\\";
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.TypeAsync(text);

        Assert.AreEqual(text, await ValueAsync("#f"));
    }

    [TestMethod]
    public async Task TypeAsync_Newline_InTextarea_InsertsLineBreak()
    {
        await LoadAsync("<textarea id='t'></textarea>");
        await FocusAsync("#t");

        await _page.Keyboard.TypeAsync("a\nb");

        Assert.AreEqual("a\nb", await ValueAsync("#t"));
    }

    [TestMethod]
    public async Task TypeAsync_OffLayoutCharacters_AreInserted()
    {
        const string text = "café \U0001F600 中文";
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.TypeAsync(text);

        Assert.AreEqual(text, await ValueAsync("#f"));
    }

    [TestMethod]
    public async Task PressAsync_ControlA_DoesNotInsertText()
    {
        await LoadAsync("<input id='f' value='abc'>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("End");
        await _page.Keyboard.PressAsync("Control+a");

        Assert.AreEqual("abc", await ValueAsync("#f"));
    }

    [TestMethod]
    public async Task PressAsync_ShiftA_TypesUppercase()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Shift+a");
        await _page.Keyboard.PressAsync("Shift+1");

        Assert.AreEqual("A!", await ValueAsync("#f"));
    }

    [TestMethod]
    public async Task PressAsync_EditingKeys_EditTheField()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.TypeAsync("abc");
        await _page.Keyboard.PressAsync("Backspace");
        await _page.Keyboard.PressAsync("ArrowLeft");
        await _page.Keyboard.PressAsync("x");

        Assert.AreEqual("axb", await ValueAsync("#f"));
    }

    [TestMethod]
    public async Task PressAsync_Tab_MovesFocus()
    {
        await LoadAsync("<input id='a'><input id='b'>");
        await FocusAsync("#a");

        await _page.Keyboard.PressAsync("Tab");

        Assert.AreEqual("b", await _page.EvaluateAsync<string>("document.activeElement.id"));
        Assert.AreEqual(string.Empty, await ValueAsync("#a"));
    }

    [TestMethod]
    public async Task Locator_PressAsync_Enter_OnButton_ClicksIt()
    {
        await LoadAsync("<button id='b' type='button'>Cancel</button>");

        await _page.Locator("#b").PressAsync("Enter", new KeyboardPressOptions(Timeout: 5000));

        Assert.AreEqual(1, await CountAsync("clicks"));
    }
}
