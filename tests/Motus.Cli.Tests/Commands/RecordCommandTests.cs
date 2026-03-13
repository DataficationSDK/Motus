using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class RecordCommandTests
{
    private static readonly Command Cmd = RecordCommand.Build();

    [TestMethod]
    public void Parse_NoArgs_NoErrors()
    {
        var result = Cmd.Parse("");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_OutputOption_NoErrors()
    {
        var result = Cmd.Parse("--output my-test.cs");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_FrameworkOption_NoErrors()
    {
        var result = Cmd.Parse("--framework xunit");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_ConnectOption_NoErrors()
    {
        var result = Cmd.Parse("--connect ws://localhost:9222");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_AllOptions_NoErrors()
    {
        var result = Cmd.Parse("--output test.cs --framework nunit --class-name MyTest --method-name DoStuff --namespace My.Ns");
        Assert.AreEqual(0, result.Errors.Count);
    }
}
