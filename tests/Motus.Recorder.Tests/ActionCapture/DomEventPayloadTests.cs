using System.Text.Json;
using Motus.Recorder.ActionCapture;

namespace Motus.Recorder.Tests.ActionCapture;

[TestClass]
public class DomEventPayloadTests
{
    [TestMethod]
    public void Deserialize_ClickPayload_RoundTrips()
    {
        var json = """
        {
            "type": "mousedown",
            "timestamp": 1710000000000,
            "x": 100.5,
            "y": 200.3,
            "button": "left",
            "clickCount": 1,
            "modifiers": 0,
            "tagName": "BUTTON",
            "pageUrl": "https://example.com"
        }
        """;

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual("mousedown", payload.Type);
        Assert.AreEqual(100.5, payload.X);
        Assert.AreEqual(200.3, payload.Y);
        Assert.AreEqual("left", payload.Button);
        Assert.AreEqual(1, payload.ClickCount);
        Assert.AreEqual(0, payload.Modifiers);
        Assert.AreEqual("BUTTON", payload.TagName);
        Assert.AreEqual("https://example.com", payload.PageUrl);
    }

    [TestMethod]
    public void Deserialize_InputPayload_ReadsValue()
    {
        var json = """
        {
            "type": "input",
            "timestamp": 1710000000000,
            "x": 50,
            "y": 60,
            "value": "hello world",
            "tagName": "INPUT",
            "inputType": "text",
            "pageUrl": "https://example.com"
        }
        """;

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual("input", payload.Type);
        Assert.AreEqual("hello world", payload.Value);
        Assert.AreEqual("INPUT", payload.TagName);
        Assert.AreEqual("text", payload.InputType);
    }

    [TestMethod]
    public void Deserialize_KeydownPayload_ReadsKeyAndCode()
    {
        var json = """
        {
            "type": "keydown",
            "timestamp": 1710000000000,
            "key": "Enter",
            "code": "Enter",
            "modifiers": 2,
            "pageUrl": "https://example.com"
        }
        """;

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual("keydown", payload.Type);
        Assert.AreEqual("Enter", payload.Key);
        Assert.AreEqual("Enter", payload.Code);
        Assert.AreEqual(2, payload.Modifiers);
    }

    [TestMethod]
    public void Deserialize_ChangePayload_SelectWithValues()
    {
        var json = """
        {
            "type": "change",
            "timestamp": 1710000000000,
            "tagName": "SELECT",
            "selectedValues": ["opt1", "opt2"],
            "pageUrl": "https://example.com"
        }
        """;

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual("change", payload.Type);
        Assert.AreEqual("SELECT", payload.TagName);
        Assert.IsNotNull(payload.SelectedValues);
        Assert.AreEqual(2, payload.SelectedValues.Length);
        Assert.AreEqual("opt1", payload.SelectedValues[0]);
        Assert.AreEqual("opt2", payload.SelectedValues[1]);
    }

    [TestMethod]
    public void Deserialize_ChangePayload_CheckboxWithChecked()
    {
        var json = """
        {
            "type": "change",
            "timestamp": 1710000000000,
            "tagName": "INPUT",
            "inputType": "checkbox",
            "checked": true,
            "pageUrl": "https://example.com"
        }
        """;

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual(true, payload.Checked);
        Assert.AreEqual("checkbox", payload.InputType);
    }

    [TestMethod]
    public void Deserialize_ScrollPayload_ReadsScrollPositions()
    {
        var json = """
        {
            "type": "scroll",
            "timestamp": 1710000000000,
            "scrollX": 0,
            "scrollY": 500.5,
            "pageUrl": "https://example.com"
        }
        """;

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual("scroll", payload.Type);
        Assert.AreEqual(0.0, payload.ScrollX);
        Assert.AreEqual(500.5, payload.ScrollY);
    }

    [TestMethod]
    public void Deserialize_MissingOptionalFields_DefaultsToNull()
    {
        var json = """{"type": "blur", "timestamp": 1710000000000}""";

        var payload = JsonSerializer.Deserialize(json, RecorderJsonContext.Default.DomEventPayload);

        Assert.IsNotNull(payload);
        Assert.AreEqual("blur", payload.Type);
        Assert.IsNull(payload.X);
        Assert.IsNull(payload.Y);
        Assert.IsNull(payload.Button);
        Assert.IsNull(payload.Key);
        Assert.IsNull(payload.Value);
        Assert.IsNull(payload.SelectedValues);
        Assert.IsNull(payload.PageUrl);
    }
}
