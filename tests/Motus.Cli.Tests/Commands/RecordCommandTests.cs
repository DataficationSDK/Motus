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

    [TestMethod]
    public void Parse_WidthOption_NoErrors()
    {
        var result = Cmd.Parse("--width 1920");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_HeightOption_NoErrors()
    {
        var result = Cmd.Parse("--height 1080");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_ViewportOptions_NoErrors()
    {
        var result = Cmd.Parse("--width 1920 --height 1080");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_AllOptionsWithViewport_NoErrors()
    {
        var result = Cmd.Parse("--output test.cs --framework nunit --width 1440 --height 900 --class-name MyTest --method-name DoStuff --namespace My.Ns");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WidthDefault_Is1024()
    {
        var result = Cmd.Parse("");
        var widthOpt = (Option<int>)Cmd.Options.First(o => o.Name.Contains("width"));
        var value = result.GetValue(widthOpt);
        Assert.AreEqual(1024, value);
    }

    [TestMethod]
    public void Parse_HeightDefault_Is768()
    {
        var result = Cmd.Parse("");
        var heightOpt = (Option<int>)Cmd.Options.First(o => o.Name.Contains("height"));
        var value = result.GetValue(heightOpt);
        Assert.AreEqual(768, value);
    }
}
