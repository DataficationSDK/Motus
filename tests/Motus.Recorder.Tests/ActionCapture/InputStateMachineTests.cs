using System.Threading.Channels;
using Motus.Recorder.ActionCapture;
using Motus.Recorder.Records;

namespace Motus.Recorder.Tests.ActionCapture;

[TestClass]
public class InputStateMachineTests
{
    private Channel<ActionRecord> _channel = null!;
    private long _time;
    private readonly List<(TimeSpan Delay, Action Callback)> _pendingTimers = [];
    private ActionCaptureOptions _options = null!;

    [TestInitialize]
    public void Init()
    {
        _channel = Channel.CreateUnbounded<ActionRecord>();
        _time = 1_710_000_000_000;
        _pendingTimers.Clear();
        _options = new ActionCaptureOptions();
    }

    private InputStateMachine CreateMachine()
    {
        return new InputStateMachine(
            _channel.Writer,
            _options,
            clock: () => _time,
            timerFactory: (delay, callback) =>
            {
                _pendingTimers.Add((delay, callback));
                return new NoopDisposable();
            });
    }

    private void FireLatestTimer()
    {
        if (_pendingTimers.Count > 0)
        {
            var timer = _pendingTimers[^1];
            timer.Callback();
        }
    }

    private ActionRecord? TryRead()
    {
        _channel.Reader.TryRead(out var result);
        return result;
    }

    // --- Click Tests ---

    [TestMethod]
    public void MouseDownUp_WithinThreshold_EmitsClickAction()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "mousedown", X = 100, Y = 200, Button = "left",
            ClickCount = 1, Modifiers = 0, PageUrl = "https://example.com"
        });

        _time += 50; // 50ms later

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "mouseup", X = 101, Y = 201, Button = "left",
            Modifiers = 0, PageUrl = "https://example.com"
        });

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<ClickAction>(action);

        var click = (ClickAction)action;
        Assert.AreEqual("left", click.Button);
        Assert.AreEqual(1, click.ClickCount);
        Assert.AreEqual(100.0, click.X);
        Assert.AreEqual(200.0, click.Y);
        Assert.AreEqual("https://example.com", click.PageUrl);
    }

    [TestMethod]
    public void MouseDownUp_ExceedsTimeThreshold_NoAction()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "mousedown", X = 100, Y = 200, Button = "left",
            ClickCount = 1, PageUrl = "https://example.com"
        });

        _time += 500; // 500ms > 300ms threshold

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "mouseup", X = 101, Y = 201, Button = "left",
            PageUrl = "https://example.com"
        });

        Assert.IsNull(TryRead());
    }

    [TestMethod]
    public void MouseDownUp_ExceedsDistanceThreshold_NoAction()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "mousedown", X = 100, Y = 200, Button = "left",
            ClickCount = 1, PageUrl = "https://example.com"
        });

        _time += 50;

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "mouseup", X = 200, Y = 200, Button = "left", // 100px away
            PageUrl = "https://example.com"
        });

        Assert.IsNull(TryRead());
    }

    // --- Fill Tests ---

    [TestMethod]
    public void RapidInputEvents_CollapseIntoSingleFill()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "h", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });
        _time += 20;
        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "he", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });
        _time += 20;
        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "hel", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });

        // No action yet (debounce pending)
        Assert.IsNull(TryRead());

        // Fire the debounce timer
        FireLatestTimer();

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<FillAction>(action);
        Assert.AreEqual("hel", ((FillAction)action).Value);
    }

    [TestMethod]
    public void BlurEvent_FlushesFillImmediately()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "hello", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "blur", PageUrl = "https://example.com"
        });

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<FillAction>(action);
        Assert.AreEqual("hello", ((FillAction)action).Value);
    }

    [TestMethod]
    public void NonPrintableKeyDuringFill_FlushesAndEmitsKeyPress()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "test", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "keydown", Key = "Enter", Code = "Enter", Modifiers = 0,
            PageUrl = "https://example.com"
        });

        // Fill should have been flushed
        var fill = TryRead();
        Assert.IsNotNull(fill);
        Assert.IsInstanceOfType<FillAction>(fill);
        Assert.AreEqual("test", ((FillAction)fill).Value);

        // KeyPress should follow
        var keyPress = TryRead();
        Assert.IsNotNull(keyPress);
        Assert.IsInstanceOfType<KeyPressAction>(keyPress);
        Assert.AreEqual("Enter", ((KeyPressAction)keyPress).Key);
    }

    [TestMethod]
    public void PrintableKeyDuringFill_IsAbsorbed()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "a", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });

        // Printable 'b' keydown during active fill - should be absorbed
        sm.ProcessEvent(new DomEventPayload
        {
            Type = "keydown", Key = "b", Code = "KeyB", Modifiers = 0,
            PageUrl = "https://example.com"
        });

        // No KeyPressAction should have been emitted
        Assert.IsNull(TryRead());

        // Flush to get the fill
        sm.Flush();
        var fill = TryRead();
        Assert.IsNotNull(fill);
        Assert.IsInstanceOfType<FillAction>(fill);
    }

    // --- Scroll Tests ---

    [TestMethod]
    public void RapidScrollEvents_CollapseIntoSingleScroll()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "scroll", ScrollX = 0, ScrollY = 100,
            PageUrl = "https://example.com"
        });
        _time += 30;
        sm.ProcessEvent(new DomEventPayload
        {
            Type = "scroll", ScrollX = 0, ScrollY = 250,
            PageUrl = "https://example.com"
        });
        _time += 30;
        sm.ProcessEvent(new DomEventPayload
        {
            Type = "scroll", ScrollX = 0, ScrollY = 500,
            PageUrl = "https://example.com"
        });

        // No action yet
        Assert.IsNull(TryRead());

        // Fire debounce timer
        FireLatestTimer();

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<ScrollAction>(action);
        Assert.AreEqual(500.0, ((ScrollAction)action).ScrollY);
    }

    // --- Select Tests ---

    [TestMethod]
    public void ChangeOnSelect_EmitsSelectAction()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "change", TagName = "SELECT",
            SelectedValues = ["opt1", "opt2"],
            PageUrl = "https://example.com"
        });

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<SelectAction>(action);

        var select = (SelectAction)action;
        Assert.AreEqual(2, select.SelectedValues.Length);
        Assert.AreEqual("opt1", select.SelectedValues[0]);
        Assert.AreEqual("opt2", select.SelectedValues[1]);
    }

    // --- Checkbox Tests ---

    [TestMethod]
    public void ChangeOnCheckbox_EmitsCheckAction()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "change", TagName = "INPUT", InputType = "checkbox",
            Checked = true,
            PageUrl = "https://example.com"
        });

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<CheckAction>(action);
        Assert.AreEqual(true, ((CheckAction)action).Checked);
    }

    [TestMethod]
    public void ChangeOnRadio_EmitsCheckAction()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "change", TagName = "INPUT", InputType = "radio",
            Checked = true,
            PageUrl = "https://example.com"
        });

        var action = TryRead();
        Assert.IsNotNull(action);
        Assert.IsInstanceOfType<CheckAction>(action);
    }

    // --- Flush Tests ---

    [TestMethod]
    public void Flush_CompletesPendingFillAndScroll()
    {
        var sm = CreateMachine();

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "input", Value = "pending", X = 10, Y = 20,
            PageUrl = "https://example.com"
        });

        sm.ProcessEvent(new DomEventPayload
        {
            Type = "scroll", ScrollX = 0, ScrollY = 300,
            PageUrl = "https://example.com"
        });

        // Nothing emitted yet (both debouncing)
        Assert.IsNull(TryRead());

        sm.Flush();

        var fill = TryRead();
        Assert.IsNotNull(fill);
        Assert.IsInstanceOfType<FillAction>(fill);

        var scroll = TryRead();
        Assert.IsNotNull(scroll);
        Assert.IsInstanceOfType<ScrollAction>(scroll);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
