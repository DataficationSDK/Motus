using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class UpdateProtocolCommandTests
{
    private static readonly Command Cmd = UpdateProtocolCommand.Build();

    [TestMethod]
    public void Parse_NoArgs_NoErrors()
    {
        var result = Cmd.Parse("");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_VersionOption_NoErrors()
    {
        var result = Cmd.Parse("--version 0.0.1350728");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_DryRunOption_NoErrors()
    {
        var result = Cmd.Parse("--dry-run");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_OutputDirOption_NoErrors()
    {
        var result = Cmd.Parse("--output-dir ./protocol");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_AllOptions_NoErrors()
    {
        var result = Cmd.Parse("--version 0.0.1350728 --dry-run --output-dir ./proto");
        Assert.AreEqual(0, result.Errors.Count);
    }
}
