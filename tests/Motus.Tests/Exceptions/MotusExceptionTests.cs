using Motus.Abstractions;

namespace Motus.Tests.Exceptions;

[TestClass]
public class MotusExceptionTests
{
    [TestMethod]
    public void NavigationTimeoutException_IsMotusTimeoutException()
    {
        var ex = new NavigationTimeoutException(
            "https://example.com", TimeSpan.FromSeconds(30), null,
            "Navigation timed out.");

        Assert.IsInstanceOfType<MotusTimeoutException>(ex);
        Assert.IsInstanceOfType<MotusException>(ex);
    }

    [TestMethod]
    public void WaitTimeoutException_IsMotusTimeoutException()
    {
        var ex = new WaitTimeoutException(
            "visible", TimeSpan.FromSeconds(5), "false", "Wait timed out.");

        Assert.IsInstanceOfType<MotusTimeoutException>(ex);
        Assert.IsInstanceOfType<MotusException>(ex);
    }

    [TestMethod]
    public void ActionTimeoutException_IsMotusTimeoutException()
    {
        var ex = new ActionTimeoutException(
            "#btn", "visible", null, "https://example.com",
            TimeSpan.FromSeconds(30), "Actionability timed out.");

        Assert.IsInstanceOfType<MotusTimeoutException>(ex);
        Assert.IsInstanceOfType<MotusException>(ex);
    }

    [TestMethod]
    public void ElementNotFoundException_IsMotusSelectorException()
    {
        var ex = new ElementNotFoundException("#missing", "https://example.com");

        Assert.IsInstanceOfType<MotusSelectorException>(ex);
        Assert.IsInstanceOfType<MotusException>(ex);
    }

    [TestMethod]
    public void AmbiguousSelectorException_IsMotusSelectorException()
    {
        var ex = new AmbiguousSelectorException("div.item", "https://example.com", 5);

        Assert.IsInstanceOfType<MotusSelectorException>(ex);
        Assert.IsInstanceOfType<MotusException>(ex);
    }

    [TestMethod]
    public void NavigationTimeoutException_PropertiesRoundTrip()
    {
        var events = new List<string> { "GET /page", "GET /style.css" };
        var ex = new NavigationTimeoutException(
            "https://example.com", TimeSpan.FromSeconds(30), events, "Timed out.");

        Assert.AreEqual("https://example.com", ex.Url);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ex.TimeoutDuration);
        Assert.AreEqual(2, ex.LastNetworkEvents!.Count);
        Assert.AreEqual("GET /page", ex.LastNetworkEvents[0]);
    }

    [TestMethod]
    public void WaitTimeoutException_PropertiesRoundTrip()
    {
        var ex = new WaitTimeoutException(
            "URL match 'https://example.com/*'", TimeSpan.FromSeconds(10),
            "https://example.com/old", "Wait timed out.");

        Assert.AreEqual("URL match 'https://example.com/*'", ex.Condition);
        Assert.AreEqual(TimeSpan.FromSeconds(10), ex.TimeoutDuration);
        Assert.AreEqual("https://example.com/old", ex.LastEvaluatedValue);
    }

    [TestMethod]
    public void ActionTimeoutException_PropertiesRoundTrip()
    {
        var ex = new ActionTimeoutException(
            "#submit", "enabled", "disabled=true", "https://example.com/form",
            TimeSpan.FromSeconds(30), "Check failed.");

        Assert.AreEqual("#submit", ex.Selector);
        Assert.AreEqual("enabled", ex.FailedCheckName);
        Assert.AreEqual("disabled=true", ex.ElementState);
        Assert.AreEqual("https://example.com/form", ex.PageUrl);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ex.TimeoutDuration);
    }

    [TestMethod]
    public void ElementNotFoundException_PropertiesRoundTrip()
    {
        var ex = new ElementNotFoundException("#missing", "https://example.com", "<html>...</html>");

        Assert.AreEqual("#missing", ex.Selector);
        Assert.AreEqual("https://example.com", ex.PageUrl);
        Assert.AreEqual("<html>...</html>", ex.DomSnapshot);
        Assert.IsTrue(ex.Message.Contains("#missing"));
    }

    [TestMethod]
    public void AmbiguousSelectorException_PropertiesRoundTrip()
    {
        var ex = new AmbiguousSelectorException("div.item", "https://example.com", 5);

        Assert.AreEqual("div.item", ex.Selector);
        Assert.AreEqual("https://example.com", ex.PageUrl);
        Assert.AreEqual(5, ex.MatchedCount);
    }

    [TestMethod]
    public void MotusNavigationException_PropertiesRoundTrip()
    {
        var ex = new MotusNavigationException(
            "https://bad.example", "net::ERR_NAME_NOT_RESOLVED", "about:blank");

        Assert.AreEqual("https://bad.example", ex.Url);
        Assert.AreEqual("net::ERR_NAME_NOT_RESOLVED", ex.ErrorCode);
        Assert.AreEqual("about:blank", ex.PageUrl);
        Assert.IsTrue(ex.Message.Contains("net::ERR_NAME_NOT_RESOLVED"));
    }

    [TestMethod]
    public void MotusTargetClosedException_PropertiesRoundTrip()
    {
        var ex = new MotusTargetClosedException("session", "sess-123", "Target closed.");

        Assert.AreEqual("session", ex.TargetType);
        Assert.AreEqual("sess-123", ex.TargetId);
    }

    [TestMethod]
    public void MotusAssertionException_PropertiesRoundTrip()
    {
        var ex = new MotusAssertionException(
            "visible", "hidden", "#elem", "https://example.com",
            TimeSpan.FromSeconds(5), "Expected visible but was hidden.");

        Assert.AreEqual("visible", ex.Expected);
        Assert.AreEqual("hidden", ex.Actual);
        Assert.AreEqual("#elem", ex.Selector);
        Assert.AreEqual("https://example.com", ex.PageUrl);
        Assert.AreEqual(TimeSpan.FromSeconds(5), ex.AssertionTimeout);
    }

    [TestMethod]
    public void MotusProtocolException_PropertiesRoundTrip()
    {
        var ex = new MotusProtocolException(-32601, "Page.navigate", "Method not found.");

        Assert.AreEqual(-32601, ex.CdpErrorCode);
        Assert.AreEqual("Page.navigate", ex.CommandSent);
    }

    [TestMethod]
    public void Screenshot_DefaultsToNull_IsSettable()
    {
        var ex = new MotusException("test");
        Assert.IsNull(ex.Screenshot);

        var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        ex.Screenshot = bytes;
        Assert.AreEqual(4, ex.Screenshot.Length);
    }

    [TestMethod]
    public void InnerException_Propagation()
    {
        var inner = new InvalidOperationException("inner error");
        var ex = new MotusProtocolException(
            -32000, "Runtime.evaluate", "Eval failed.", inner);

        Assert.IsNotNull(ex.InnerException);
        Assert.AreSame(inner, ex.InnerException);
        Assert.AreEqual("inner error", ex.InnerException.Message);
    }

    [TestMethod]
    public void MotusTimeoutException_MessageFormatting()
    {
        var ex = new MotusTimeoutException(
            TimeSpan.FromMilliseconds(5000), "Operation timed out after 5000ms.");

        Assert.AreEqual("Operation timed out after 5000ms.", ex.Message);
        Assert.AreEqual(TimeSpan.FromMilliseconds(5000), ex.TimeoutDuration);
    }
}
