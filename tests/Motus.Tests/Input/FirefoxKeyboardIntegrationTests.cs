using Motus.Abstractions;

namespace Motus.Tests.Input;

/// <summary>
/// Drives the real keyboard against a Firefox page over WebDriver BiDi and checks what the page
/// saw. Focus and results go through script so the tests depend on nothing but the keyboard.
/// </summary>
[TestClass]
[TestCategory("Integration")]
[TestCategory("Firefox")]
public class FirefoxKeyboardIntegrationTests
{
    private IBrowser? _browser;
    private IPage _page = null!;

    [TestInitialize]
    public async Task Setup()
    {
        try
        {
            BrowserFinder.Resolve(BrowserChannel.Firefox, executablePath: null);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("Firefox not found on this machine.");
        }

        _browser = await MotusLauncher.LaunchAsync(
            new LaunchOptions { Headless = true, Channel = BrowserChannel.Firefox });

        var context = await _browser.NewContextAsync();
        _page = await context.NewPageAsync();
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
                document.addEventListener('keydown', e => window.keydowns.push(
                    e.key + '/' + e.code + '/' + e.location + (e.shiftKey ? '/shift' : '')), true);
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

    private Task<string> KeydownsAsync()
        => _page.EvaluateAsync<string>("window.keydowns.join(',')");

    [TestMethod]
    public async Task PressAsync_Enter_InTextInput_SubmitsForm()
    {
        await LoadAsync("<form><input id='f' name='q' value='x'><button type='submit'>Go</button></form>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Enter");

        Assert.AreEqual("Enter/Enter/0", await KeydownsAsync());
        Assert.AreEqual(1, await _page.EvaluateAsync<int>("window.submits"));
    }

    [TestMethod]
    public async Task PressAsync_NumpadEnter_OnFocusedButton_ClicksIt()
    {
        await LoadAsync("<button id='b' type='button'>OK</button>");
        await FocusAsync("#b");

        await _page.Keyboard.PressAsync("NumpadEnter");

        StringAssert.StartsWith(await KeydownsAsync(), "Enter/NumpadEnter/");
        Assert.AreEqual(1, await _page.EvaluateAsync<int>("window.clicks"));
    }

    [TestMethod]
    public async Task TypeAsync_Newline_InTextarea_InsertsLineBreak()
    {
        await LoadAsync("<textarea id='t'></textarea>");
        await FocusAsync("#t");

        await _page.Keyboard.TypeAsync("one\ntwo");

        Assert.AreEqual("one\ntwo", await ValueAsync("#t"));
    }

    [TestMethod]
    public async Task PressAsync_EditingKeys_EditTheField()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.TypeAsync("abc");
        await _page.Keyboard.PressAsync("Backspace");
        await _page.Keyboard.PressAsync("ArrowLeft");
        await _page.Keyboard.PressAsync("ArrowLeft");
        await _page.Keyboard.PressAsync("Delete");
        await _page.Keyboard.PressAsync("ArrowRight");
        await _page.Keyboard.TypeAsync("z");

        Assert.AreEqual("bz", await ValueAsync("#f"));
    }

    [TestMethod]
    public async Task PressAsync_ShiftA_TypesUppercase()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Shift+a");
        await _page.Keyboard.PressAsync("ShiftRight+1");

        Assert.AreEqual("A!", await ValueAsync("#f"));
        Assert.AreEqual(
            "Shift/ShiftLeft/1/shift,A/KeyA/0/shift,Shift/ShiftRight/2/shift,!/Digit1/0/shift",
            await KeydownsAsync());
    }

    [TestMethod]
    public async Task PressAsync_ControlA_DoesNotInsertText()
    {
        await LoadAsync("<input id='f' value='hello'>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Control+a");
        await _page.Keyboard.PressAsync("ControlRight+a");

        Assert.AreEqual("hello", await ValueAsync("#f"));
        Assert.AreEqual(
            "Control/ControlLeft/1,a/KeyA/0,Control/ControlRight/2,a/KeyA/0",
            await KeydownsAsync());
    }

    [TestMethod]
    public async Task PressAsync_NamedKeys_ReachThePageAsThemselves()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        foreach (var key in new[] { "Escape", "Tab", "ArrowUp", "ArrowDown", "Home", "End", "PageUp", "PageDown", "Insert", "F1", "F12", "Alt", "Meta" })
        {
            await FocusAsync("#f");
            await _page.Keyboard.PressAsync(key);
        }

        Assert.AreEqual(
            "Escape/Escape/0,Tab/Tab/0,ArrowUp/ArrowUp/0,ArrowDown/ArrowDown/0,Home/Home/0,End/End/0,PageUp/PageUp/0," +
            "PageDown/PageDown/0,Insert/Insert/0,F1/F1/0,F12/F12/0,Alt/AltLeft/1,Meta/MetaLeft/1",
            await KeydownsAsync());
    }

    [TestMethod]
    public async Task PressAsync_NumpadDigit_TypesDigitFromTheNumpad()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        await _page.Keyboard.PressAsync("Numpad7");
        await _page.Keyboard.PressAsync("NumpadAdd");

        Assert.AreEqual("7+", await ValueAsync("#f"));
        Assert.AreEqual("7/Numpad7/3,+/NumpadAdd/3", await KeydownsAsync());
    }

    [TestMethod]
    public async Task PressAsync_Tab_MovesFocus()
    {
        await LoadAsync("<input id='a'><input id='b'>");
        await FocusAsync("#a");

        await _page.Keyboard.PressAsync("Tab");

        Assert.AreEqual("b", await _page.EvaluateAsync<string>("document.activeElement.id"));
    }

    [TestMethod]
    public async Task PressAsync_KeyWithoutWebDriverValue_ThrowsNotSupported()
    {
        await LoadAsync("<input id='f'>");
        await FocusAsync("#f");

        var ex = await Assert.ThrowsExceptionAsync<NotSupportedException>(
            () => _page.Keyboard.PressAsync("CapsLock"));

        StringAssert.Contains(ex.Message, "CapsLock");

        // The failed key never counted as held, so the next key arrives on its own.
        await _page.Keyboard.PressAsync("a");
        Assert.AreEqual("a", await ValueAsync("#f"));
    }
}
