namespace Motus.Tests.Selectors;

[TestClass]
public class SelectorPrefixParserTests
{
    [TestMethod]
    public void UnprefixedSelector_DefaultsToCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("div.container");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("div.container", expression);
    }

    [TestMethod]
    public void CssPrefix_ParsedCorrectly()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("css=div.container");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("div.container", expression);
    }

    [TestMethod]
    public void XPathPrefix_ParsedCorrectly()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("xpath=//div[@class='test']");
        Assert.AreEqual("xpath", prefix);
        Assert.AreEqual("//div[@class='test']", expression);
    }

    [TestMethod]
    public void TextPrefix_ParsedCorrectly()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("text=Hello World");
        Assert.AreEqual("text", prefix);
        Assert.AreEqual("Hello World", expression);
    }

    [TestMethod]
    public void RolePrefix_ParsedCorrectly()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("""role=button[name="Submit"]""");
        Assert.AreEqual("role", prefix);
        Assert.AreEqual("""button[name="Submit"]""", expression);
    }

    [TestMethod]
    public void DataTestIdPrefix_ParsedCorrectly()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("data-testid=login-btn");
        Assert.AreEqual("data-testid", prefix);
        Assert.AreEqual("login-btn", expression);
    }

    [TestMethod]
    public void AttributeSelector_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("[data-testid=\"foo\"]");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("[data-testid=\"foo\"]", expression);
    }

    [TestMethod]
    public void ClassSelector_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix(".my-class=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual(".my-class=something", expression);
    }

    [TestMethod]
    public void IdSelector_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("#my-id=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("#my-id=something", expression);
    }

    [TestMethod]
    public void PseudoSelector_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("div:nth-child(2)=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("div:nth-child(2)=something", expression);
    }

    [TestMethod]
    public void ChildCombinator_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("div > span=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("div > span=something", expression);
    }

    [TestMethod]
    public void SiblingCombinator_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("div ~ span=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("div ~ span=something", expression);
    }

    [TestMethod]
    public void AdjacentSiblingCombinator_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("div + span=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("div + span=something", expression);
    }

    [TestMethod]
    public void CommaSeparated_TreatedAsCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("h1,h2=something");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("h1,h2=something", expression);
    }

    [TestMethod]
    public void NoEquals_DefaultsToCss()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("#simple-id");
        Assert.AreEqual("css", prefix);
        Assert.AreEqual("#simple-id", expression);
    }

    [TestMethod]
    public void EmptyExpression_AfterPrefix()
    {
        var (prefix, expression) = Motus.Locator.ParsePrefix("text=");
        Assert.AreEqual("text", prefix);
        Assert.AreEqual("", expression);
    }
}
