using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class ScreenshotCommandTests
{
    private static readonly Command Cmd = ScreenshotCommand.Build();

    [TestMethod]
    public void Parse_WithUrl_NoErrors()
    {
        var result = Cmd.Parse("https://example.com");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithOutputOption_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --output page.png");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithFullPageFlag_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --full-page");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithDimensions_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --width 1920 --height 1080");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_MissingUrl_HasErrors()
    {
        var result = Cmd.Parse("");
        Assert.IsTrue(result.Errors.Count > 0);
    }
}
