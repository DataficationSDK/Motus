using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class RepairSuggestionPipelineTests
{
    [TestMethod]
    public void Translate_DataTestId_EmitsGetByTestId()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "data-testid", rawSelector: "data-testid=submit");
        Assert.AreEqual("GetByTestId(\"submit\")", result);
    }

    [TestMethod]
    public void Translate_Role_NoName_EmitsGetByRoleWithoutName()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "role", rawSelector: "role=button");
        Assert.AreEqual("GetByRole(\"button\")", result);
    }

    [TestMethod]
    public void Translate_Role_WithName_EmitsGetByRoleWithNameArgument()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "role", rawSelector: "role=button[name=\"Sign in\"]");
        Assert.AreEqual("GetByRole(\"button\", name: \"Sign in\")", result);
    }

    [TestMethod]
    public void Translate_Text_EmitsGetByText()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "text", rawSelector: "text=Submit");
        Assert.AreEqual("GetByText(\"Submit\")", result);
    }

    [TestMethod]
    public void Translate_Css_EmitsLocatorWithPrefix()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "css", rawSelector: "css=#foo");
        Assert.AreEqual("Locator(\"css=#foo\")", result);
    }

    [TestMethod]
    public void Translate_Xpath_EmitsLocatorWithPrefix()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "xpath", rawSelector: "xpath=/html/body/button[1]");
        Assert.AreEqual("Locator(\"xpath=/html/body/button[1]\")", result);
    }

    [TestMethod]
    public void Translate_EscapesQuotesInValue()
    {
        var result = RepairSuggestionPipeline.TranslateToLocatorCall(
            strategyName: "data-testid", rawSelector: "data-testid=say \"hi\"");
        Assert.AreEqual("GetByTestId(\"say \\\"hi\\\"\")", result);
    }

    [TestMethod]
    public void ConfidenceMapping_FromQuality_MapsCorrectly()
    {
        Assert.AreEqual(Confidence.High,
            ConfidenceMapping.FromQuality(FingerprintMatchQuality.Hash));
        Assert.AreEqual(Confidence.Medium,
            ConfidenceMapping.FromQuality(FingerprintMatchQuality.Attributes));
        Assert.AreEqual(Confidence.Low,
            ConfidenceMapping.FromQuality(FingerprintMatchQuality.Ancestor));
    }
}
