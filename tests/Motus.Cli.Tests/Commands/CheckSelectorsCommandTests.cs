using System.CommandLine;
using Motus.Cli.Commands;

namespace Motus.Cli.Tests.Commands;

[TestClass]
public class CheckSelectorsCommandTests
{
    private static readonly Command Cmd = CheckSelectorsCommand.Build();

    [TestMethod]
    public void Parse_WithGlobAndBaseUrl_NoErrors()
    {
        var result = Cmd.Parse("**/*.cs --base-url https://example.com");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithGlobAndManifest_NoErrors()
    {
        var result = Cmd.Parse("**/*.cs --manifest foo.selectors.json");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithCiFlag_NoErrors()
    {
        var result = Cmd.Parse("**/*.cs --base-url https://x --ci");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_WithJsonFlag_NoErrors()
    {
        var result = Cmd.Parse("**/*.cs --base-url https://x --json out.json");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_AllFlags_NoErrors()
    {
        var result = Cmd.Parse("tests/**/*.cs --manifest m.json --base-url https://x --ci --json out.json");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public void Parse_MissingGlob_HasErrors()
    {
        var result = Cmd.Parse("");
        Assert.IsTrue(result.Errors.Count > 0);
    }

    [TestMethod]
    public void Parse_WithInteractive_NoErrors()
    {
        var result = Cmd.Parse("**/*.cs --manifest m.json --interactive");
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public async Task Invoke_InteractiveWithoutManifest_ReturnsTwo()
    {
        var origErr = Console.Error;
        try
        {
            Console.SetError(new StringWriter());
            var result = Cmd.Parse("**/*.cs --base-url https://x --interactive");
            var exit = await result.InvokeAsync();
            Assert.AreEqual(2, exit);
        }
        finally
        {
            Console.SetError(origErr);
        }
    }

    [TestMethod]
    public async Task Invoke_InteractiveWithFix_ReturnsTwo()
    {
        var origErr = Console.Error;
        try
        {
            Console.SetError(new StringWriter());
            var result = Cmd.Parse("**/*.cs --manifest m.json --interactive --fix");
            var exit = await result.InvokeAsync();
            Assert.AreEqual(2, exit);
        }
        finally
        {
            Console.SetError(origErr);
        }
    }
}
