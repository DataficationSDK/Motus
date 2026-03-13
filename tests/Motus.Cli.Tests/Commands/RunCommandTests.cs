using System.CommandLine;
using System.CommandLine.Parsing;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class RunCommandTests
{
    private static readonly Command Cmd = RunCommand.Build();

    [TestMethod]
    public void Parse_NoArgs_NoErrors()
    {
        var result = Cmd.Parse("");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithAssembly_NoErrors()
    {
        var result = Cmd.Parse("tests.dll");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_MultipleAssemblies_NoErrors()
    {
        var result = Cmd.Parse("a.dll b.dll c.dll");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_FilterOption_NoErrors()
    {
        var result = Cmd.Parse("tests.dll --filter SomeTest");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_ReporterOption_NoErrors()
    {
        var result = Cmd.Parse("tests.dll --reporter junit:results.xml");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WorkersOption_NoErrors()
    {
        var result = Cmd.Parse("tests.dll --workers 4");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_VisualFlag_NoErrors()
    {
        var result = Cmd.Parse("tests.dll --visual");
        Assert.AreEqual(0, result.Errors.Count);
    }
}
