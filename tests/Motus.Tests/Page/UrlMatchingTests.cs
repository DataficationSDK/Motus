namespace Motus.Tests.Page;

/// <summary>
/// The two ways a URL pattern is matched: against the URL of a page or frame, where a string with
/// no wildcard has to be the whole URL, and against a request URL, where it may be any part of it.
/// </summary>
[TestClass]
public class UrlMatchingTests
{
    // ── Page and frame URLs ──

    [TestMethod]
    public void Whole_ExactUrl_Matches() =>
        Assert.IsTrue(Motus.Page.UrlMatchesWhole("https://example.test/", "https://example.test/"));

    /// <summary>
    /// A site's root is a prefix of every other URL on the site, so a containment check let a wait
    /// for the root return from any page.
    /// </summary>
    [TestMethod]
    [DataRow("https://example.test/account", "https://example.test/")]
    [DataRow("https://example.test/orders/42/edit", "https://example.test/orders")]
    [DataRow("https://example.test/orders/42/edit", "/orders")]
    [DataRow("https://example.test/orders/42/edit", "orders/42")]
    public void Whole_PlainString_DoesNotMatchAUrlThatOnlyContainsIt(string url, string pattern) =>
        Assert.IsFalse(Motus.Page.UrlMatchesWhole(url, pattern));

    /// <summary>
    /// The browser reports a bare origin with a trailing slash and a host in lower case, so a
    /// base URL written without the slash still names the page.
    /// </summary>
    [TestMethod]
    [DataRow("http://localhost:5000/", "http://localhost:5000")]
    [DataRow("https://example.test/", "https://EXAMPLE.test")]
    [DataRow("https://example.test/a%20b", "https://example.test/a b")]
    public void Whole_PlainString_MatchesTheBrowsersFormOfTheSameUrl(string url, string pattern) =>
        Assert.IsTrue(Motus.Page.UrlMatchesWhole(url, pattern));

    [TestMethod]
    [DataRow("https://example.test/account", "https://example.test")]
    [DataRow("file:///orders", "/orders")]
    public void Whole_Normalizing_DoesNotWidenTheMatch(string url, string pattern) =>
        Assert.IsFalse(Motus.Page.UrlMatchesWhole(url, pattern));

    [TestMethod]
    public void Whole_QueryPrefix_DoesNotMatchALongerValue()
    {
        Assert.IsFalse(Motus.Page.UrlMatchesWhole(
            "https://example.test/orders?page=10", "https://example.test/orders?page=1"));
        Assert.IsTrue(Motus.Page.UrlMatchesWhole(
            "https://example.test/orders?page=1", "https://example.test/orders?page=1"));
    }

    [TestMethod]
    public void Whole_IsCaseSensitive() =>
        Assert.IsFalse(Motus.Page.UrlMatchesWhole("https://example.test/Orders", "https://example.test/orders"));

    [TestMethod]
    [DataRow("https://example.test/orders/42", "**/orders/*")]
    [DataRow("https://example.test/orders/42/edit", "**/orders/*")]
    [DataRow("https://example.test/orders/42/edit", "**/orders**")]
    [DataRow("https://example.test/orders?page=10", "**/orders**")]
    [DataRow("https://example.test/", "https://example.test/*")]
    [DataRow("https://example.test/a/b/c", "https://example.test/*")]
    [DataRow("https://example.test/app#done", "**#done")]
    [DataRow("https://www.iana.org/help/example-domains", "*iana.org*")]
    public void Whole_Glob_MatchesTheWholeUrl(string url, string pattern) =>
        Assert.IsTrue(Motus.Page.UrlMatchesWhole(url, pattern));

    [TestMethod]
    [DataRow("https://example.test/orders/42/edit", "**/orders/42")]
    [DataRow("https://example.test/orders?page=2", "**/orders")]
    [DataRow("https://other.test/orders/42", "https://example.test/*")]
    public void Whole_Glob_ThatCoversOnlyPartOfTheUrl_DoesNotMatch(string url, string pattern) =>
        Assert.IsFalse(Motus.Page.UrlMatchesWhole(url, pattern));

    /// <summary>
    /// Only <c>*</c> is glob syntax. A query string's <c>?</c>, a brace list and a character class
    /// stay the literal characters they are, as they do for routes.
    /// </summary>
    [TestMethod]
    public void Whole_Glob_TreatsEverythingButTheStarLiterally()
    {
        Assert.IsTrue(Motus.Page.UrlMatchesWhole("https://example.test/list?page=1", "**/list?page=1"));
        Assert.IsFalse(Motus.Page.UrlMatchesWhole("https://example.test/listXpage=1", "**/list?page=1"));
        Assert.IsFalse(Motus.Page.UrlMatchesWhole("https://example.test/a.png", "**/a.{png,jpg}"));
        Assert.IsFalse(Motus.Page.UrlMatchesWhole("https://example.test/a", "**/[a]"));
    }

    // ── Request URLs (routes, request and response waits) ──

    /// <summary>
    /// Routes and request waits keep their documented substring fallback; only the page and frame
    /// URL waits and the URL assertion moved to whole-URL matching.
    /// </summary>
    [TestMethod]
    public void Request_PlainString_StillMatchesAsASubstring()
    {
        Assert.IsTrue(Motus.Page.UrlMatchesStatic("https://example.test/api/orders/42", "/api/orders"));
        Assert.IsTrue(Motus.Page.UrlMatchesStatic("https://example.test/api/orders", "https://example.test/api/orders"));
        Assert.IsFalse(Motus.Page.UrlMatchesStatic("https://example.test/api/users", "/api/orders"));
    }

    [TestMethod]
    public void Request_Glob_MatchesTheWholeUrl()
    {
        Assert.IsTrue(Motus.Page.UrlMatchesStatic("https://example.test/api/orders?page=2", "**/api/**"));
        Assert.IsFalse(Motus.Page.UrlMatchesStatic("https://example.test/api/orders?page=2", "**/api/orders"));
    }
}
