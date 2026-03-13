using Motus.Recorder.CodeEmit;
using Motus.Recorder.Records;

namespace Motus.Recorder.Tests.CodeEmit;

[TestClass]
public class CodeEmitterTests
{
    private static readonly DateTimeOffset Ts = new(2024, 3, 10, 12, 0, 0, TimeSpan.Zero);
    private const string Url = "https://example.com";

    private readonly CodeEmitter _emitter = new();

    private static ResolvedAction Resolved(ActionRecord action, string? selector = null)
        => new(action, selector);

    // ---- Per-action-type tests ----

    [TestMethod]
    public void ClickAction_EmitsLocatorClick()
    {
        var actions = new[]
        {
            Resolved(new ClickAction(Ts, Url, null, 100, 200, "left", 1, 0), "#btn")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Locator("#btn").ClickAsync();"""));
    }

    [TestMethod]
    public void FillAction_EmitsLocatorFill()
    {
        var actions = new[]
        {
            Resolved(new FillAction(Ts, Url, null, 10, 20, "hello world"), "#input")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Locator("#input").FillAsync("hello world");"""));
    }

    [TestMethod]
    public void KeyPressAction_EmitsKeyboardPress()
    {
        var actions = new[]
        {
            Resolved(new KeyPressAction(Ts, Url, null, null, null, "Enter", "Enter", 0))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Keyboard.PressAsync("Enter");"""));
    }

    [TestMethod]
    public void NavigationAction_EmitsGotoAsync()
    {
        var actions = new[]
        {
            Resolved(new NavigationAction(Ts, Url, null, null, null, "https://example.com/page2"))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.GotoAsync("https://example.com/page2");"""));
    }

    [TestMethod]
    public void SelectAction_EmitsSelectOption()
    {
        var actions = new[]
        {
            Resolved(new SelectAction(Ts, Url, null, 10, 20, ["option1"]), "#dropdown")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Locator("#dropdown").SelectOptionAsync("option1");"""));
    }

    [TestMethod]
    public void CheckAction_EmitsCheckAsync()
    {
        var actions = new[]
        {
            Resolved(new CheckAction(Ts, Url, null, 10, 20, true), "#checkbox")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Locator("#checkbox").CheckAsync();"""));
    }

    [TestMethod]
    public void CheckAction_Unchecked_EmitsUncheckAsync()
    {
        var actions = new[]
        {
            Resolved(new CheckAction(Ts, Url, null, 10, 20, false), "#checkbox")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Locator("#checkbox").UncheckAsync();"""));
    }

    [TestMethod]
    public void FileUploadAction_EmitsSetInputFiles()
    {
        var actions = new[]
        {
            Resolved(new FileUploadAction(Ts, Url, null, 10, 20, ["file.txt"]), "#upload")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""await page.Locator("#upload").SetInputFilesAsync("file.txt");"""));
    }

    [TestMethod]
    public void DialogAction_EmitsDialogHandler()
    {
        var actions = new[]
        {
            Resolved(new DialogAction(Ts, Url, null, null, null, "alert", true, null))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("page.Dialog += (_, d) => d.AcceptAsync();"));
    }

    [TestMethod]
    public void ScrollAction_EmitsMouseWheel()
    {
        var actions = new[]
        {
            Resolved(new ScrollAction(Ts, Url, null, null, null, 0, 300))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("await page.Mouse.WheelAsync(0, 300);"));
    }

    // ---- Framework tests ----

    [TestMethod]
    public void MSTest_ProducesValidBoilerplate()
    {
        var options = new CodeEmitOptions
        {
            Framework = "mstest",
            TestClassName = "MyTest",
            TestMethodName = "Scenario1",
            Namespace = "Tests.Gen"
        };
        var code = _emitter.Emit([], options);

        Assert.IsTrue(code.Contains("using Motus.Testing.MSTest;"));
        Assert.IsTrue(code.Contains("[TestClass]"));
        Assert.IsTrue(code.Contains("public class MyTest : MotusTestBase"));
        Assert.IsTrue(code.Contains("[TestMethod]"));
        Assert.IsTrue(code.Contains("public async Task Scenario1()"));
        Assert.IsTrue(code.Contains("namespace Tests.Gen;"));
    }

    [TestMethod]
    public void XUnit_ProducesValidBoilerplate()
    {
        var options = new CodeEmitOptions
        {
            Framework = "xunit",
            TestClassName = "MyTest",
            TestMethodName = "Scenario1",
            Namespace = "Tests.Gen"
        };
        var code = _emitter.Emit([], options);

        Assert.IsTrue(code.Contains("using Motus.Testing.xUnit;"));
        Assert.IsTrue(code.Contains("[Collection(nameof(MotusCollection))]"));
        Assert.IsTrue(code.Contains("public class MyTest : IAsyncLifetime"));
        Assert.IsTrue(code.Contains("[Fact]"));
        Assert.IsTrue(code.Contains("public async Task Scenario1()"));
        Assert.IsTrue(code.Contains("BrowserContextFixture"));
    }

    [TestMethod]
    public void NUnit_ProducesValidBoilerplate()
    {
        var options = new CodeEmitOptions
        {
            Framework = "nunit",
            TestClassName = "MyTest",
            TestMethodName = "Scenario1",
            Namespace = "Tests.Gen"
        };
        var code = _emitter.Emit([], options);

        Assert.IsTrue(code.Contains("using Motus.Testing.NUnit;"));
        Assert.IsTrue(code.Contains("[TestFixture]"));
        Assert.IsTrue(code.Contains("public class MyTest : MotusTestBase"));
        Assert.IsTrue(code.Contains("[Test]"));
        Assert.IsTrue(code.Contains("public async Task Scenario1()"));
    }

    // ---- Null selector fallback ----

    [TestMethod]
    public void NullSelector_EmitsTodoComment()
    {
        var actions = new[]
        {
            Resolved(new ClickAction(Ts, Url, null, 50, 75, "left", 1, 0), null)
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("// TODO"));
        Assert.IsTrue(code.Contains("(50, 75)"));
    }

    // ---- String escaping ----

    [TestMethod]
    public void FillValue_WithSpecialChars_IsEscaped()
    {
        var actions = new[]
        {
            Resolved(new FillAction(Ts, Url, null, 10, 20, "line1\nline2\t\"quoted\""), "#input")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("\\n"));
        Assert.IsTrue(code.Contains("\\t"));
        Assert.IsTrue(code.Contains("\\\"quoted\\\""));
    }

    [TestMethod]
    public void NavigationUrl_WithSpecialChars_IsEscaped()
    {
        var actions = new[]
        {
            Resolved(new NavigationAction(Ts, Url, null, null, null, "https://example.com/path?q=a&b=c"))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("https://example.com/path?q=a&b=c"));
    }

    [TestMethod]
    public void SelectAction_MultipleValues_EmitsArray()
    {
        var actions = new[]
        {
            Resolved(new SelectAction(Ts, Url, null, 10, 20, ["a", "b", "c"]), "#multi")
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("new[]"));
        Assert.IsTrue(code.Contains("\"a\""));
        Assert.IsTrue(code.Contains("\"b\""));
        Assert.IsTrue(code.Contains("\"c\""));
    }

    [TestMethod]
    public void DialogAction_Dismiss_EmitsDismiss()
    {
        var actions = new[]
        {
            Resolved(new DialogAction(Ts, Url, null, null, null, "confirm", false, null))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("d.DismissAsync()"));
    }

    [TestMethod]
    public void DialogAction_WithPromptText_EmitsAcceptWithText()
    {
        var actions = new[]
        {
            Resolved(new DialogAction(Ts, Url, null, null, null, "prompt", true, "user input"))
        };

        var code = _emitter.Emit(actions);
        Assert.IsTrue(code.Contains("""d.AcceptAsync("user input")"""));
    }
}
