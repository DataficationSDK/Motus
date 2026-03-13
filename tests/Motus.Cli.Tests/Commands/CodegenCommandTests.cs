using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class CodegenCommandTests
{
    private static readonly Command Cmd = CodegenCommand.Build();

    [TestMethod]
    public void Parse_WithSingleUrl_NoErrors()
    {
        var result = Cmd.Parse("https://example.com");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithMultipleUrls_NoErrors()
    {
        var result = Cmd.Parse("https://example.com https://example.com/login");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithOutputOption_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --output /tmp/pom");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithNamespaceOption_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --namespace MyApp.Pages");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithSelectorPriority_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --selector-priority testid,role,text,css");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithAllOptions_NoErrors()
    {
        var result = Cmd.Parse(
            "https://example.com https://example.com/login --output /tmp --namespace MyApp.Pages --selector-priority testid,css");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_MissingUrl_HasErrors()
    {
        var result = Cmd.Parse("");
        Assert.IsTrue(result.Errors.Count > 0);
    }
}
