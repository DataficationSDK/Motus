using System.CommandLine;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class TrxCommandTests
{
    private static readonly Command Cmd = TrxCommand.Build();

    [TestMethod]
    public void Parse_ShowWithFile_NoErrors()
    {
        var result = Cmd.Parse("show results.trx");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_ShowWithFileAndPort_NoErrors()
    {
        var result = Cmd.Parse("show results.trx --port 5400");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_ShowMissingFile_HasError()
    {
        var result = Cmd.Parse("show");
        Assert.IsTrue(result.Errors.Count > 0, "Expected missing-argument error when no file provided");
    }

    [TestMethod]
    public void Parse_PortDefaultsTo5300()
    {
        var portOpt = (Option<int>)Cmd.Subcommands.Single(c => c.Name == "show").Options.Single(o => o.Name == "--port");
        var result = Cmd.Parse("show results.trx");
        Assert.AreEqual(5300, result.GetValue(portOpt));
    }

    [TestMethod]
    public void Parse_PortOverrideBindsValue()
    {
        var portOpt = (Option<int>)Cmd.Subcommands.Single(c => c.Name == "show").Options.Single(o => o.Name == "--port");
        var result = Cmd.Parse("show results.trx --port 5400");
        Assert.AreEqual(5400, result.GetValue(portOpt));
    }
}
