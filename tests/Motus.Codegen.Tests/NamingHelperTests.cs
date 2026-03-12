using Motus.Codegen.Emit;

namespace Motus.Codegen.Tests;

[TestClass]
public class NamingHelperTests
{
    [DataTestMethod]
    [DataRow("navigate", "Navigate")]
    [DataRow("frameNavigated", "FrameNavigated")]
    [DataRow("address_bar", "AddressBar")]
    [DataRow("ch-device-memory", "ChDeviceMemory")]
    [DataRow("URL", "URL")]
    [DataRow("parentId", "ParentId")]
    [DataRow("", "")]
    public void ToPascalCase_ConvertsCorrectly(string input, string expected)
    {
        Assert.AreEqual(expected, NamingHelper.ToPascalCase(input));
    }

    [DataTestMethod]
    [DataRow("event", "@event")]
    [DataRow("object", "@object")]
    [DataRow("params", "@params")]
    [DataRow("string", "@string")]
    [DataRow("ref", "@ref")]
    [DataRow("class", "@class")]
    [DataRow("frame", "frame")]
    [DataRow("navigate", "navigate")]
    public void SanitizeIdentifier_HandlesKeywords(string input, string expected)
    {
        Assert.AreEqual(expected, NamingHelper.SanitizeIdentifier(input));
    }

    [DataTestMethod]
    [DataRow("frameId", "FrameId")]
    [DataRow("address_bar", "AddressBar")]
    [DataRow("event", "Event")]
    public void ToSafeIdentifier_CombinesPascalCaseAndSanitize(string input, string expected)
    {
        Assert.AreEqual(expected, NamingHelper.ToSafeIdentifier(input));
    }

    [DataTestMethod]
    [DataRow("FrameId", "frameId")]
    [DataRow("URL", "uRL")]
    [DataRow("object", "@object")]
    public void ToParameterName_ConvertsToCamelCase(string input, string expected)
    {
        Assert.AreEqual(expected, NamingHelper.ToParameterName(input));
    }
}
