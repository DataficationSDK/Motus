using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class PdfCommandTests
{
    private static readonly Command Cmd = PdfCommand.Build();

    [TestMethod]
    public void Parse_WithUrl_NoErrors()
    {
        var result = Cmd.Parse("https://example.com");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithOutputOption_NoErrors()
    {
        var result = Cmd.Parse("https://example.com --output report.pdf");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_MissingUrl_HasErrors()
    {
        var result = Cmd.Parse("");
        Assert.IsTrue(result.Errors.Count > 0);
    }
}
