using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class InstallCommandTests
{
    private static readonly Command Cmd = InstallCommand.Build();

    [TestMethod]
    public void Parse_NoArgs_NoErrors()
    {
        var result = Cmd.Parse("");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_ChannelOption_NoErrors()
    {
        var result = Cmd.Parse("--channel chrome");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_RevisionOption_NoErrors()
    {
        var result = Cmd.Parse("--revision 1234567");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_PathOption_NoErrors()
    {
        var result = Cmd.Parse("--path /tmp/browsers");
        Assert.AreEqual(0, result.Errors.Count);
    }
}
