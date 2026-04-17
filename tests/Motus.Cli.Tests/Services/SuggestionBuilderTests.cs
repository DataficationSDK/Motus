using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class SuggestionBuilderTests
{
    private static FingerprintCandidate Make(
        string tag = "button",
        Dictionary<string, string>? attrs = null,
        string? visibleText = null,
        string ancestorPath = "body > div")
        => new(tag, attrs ?? new Dictionary<string, string>(), visibleText, ancestorPath);

    [TestMethod]
    public void Build_DataTestIdPresent_ReturnsGetByTestId()
    {
        var s = SuggestionBuilder.Build(Make(attrs: new() { ["data-testid"] = "submit" }));
        Assert.AreEqual("GetByTestId(\"submit\")", s);
    }

    [TestMethod]
    public void Build_IdPresent_ReturnsLocatorWithHash()
    {
        var s = SuggestionBuilder.Build(Make(attrs: new() { ["id"] = "email" }));
        Assert.AreEqual("Locator(\"#email\")", s);
    }

    [TestMethod]
    public void Build_AriaLabelPresent_ReturnsGetByLabel()
    {
        var s = SuggestionBuilder.Build(Make(attrs: new() { ["aria-label"] = "Email" }));
        Assert.AreEqual("GetByLabel(\"Email\")", s);
    }

    [TestMethod]
    public void Build_RoleWithText_ReturnsGetByRoleWithName()
    {
        var s = SuggestionBuilder.Build(Make(
            attrs: new() { ["role"] = "button" },
            visibleText: "Sign in"));
        Assert.AreEqual("GetByRole(\"button\", name: \"Sign in\")", s);
    }

    [TestMethod]
    public void Build_OnlyVisibleText_ReturnsGetByText()
    {
        var s = SuggestionBuilder.Build(Make(visibleText: "Submit"));
        Assert.AreEqual("GetByText(\"Submit\")", s);
    }

    [TestMethod]
    public void Build_NoSignals_FallsBackToTag()
    {
        var s = SuggestionBuilder.Build(Make(tag: "button"));
        Assert.AreEqual("Locator(\"button\")", s);
    }

    [TestMethod]
    public void Build_PrefersTestIdOverId()
    {
        var s = SuggestionBuilder.Build(Make(attrs: new()
        {
            ["data-testid"] = "t",
            ["id"] = "i",
            ["aria-label"] = "l",
        }));
        Assert.AreEqual("GetByTestId(\"t\")", s);
    }
}
