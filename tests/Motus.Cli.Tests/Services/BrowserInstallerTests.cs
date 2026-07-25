using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class BrowserInstallerTests
{
    // A browser Motus unzips into a user's directory is not reachable by the sandbox the way an
    // installed one is, and the identifier and flags below are what close that gap. They are
    // asserted literally because nothing else in a test run can tell a wrong identifier from a
    // right one: the machine that would notice is a Windows machine running a browser.
    [TestMethod]
    public void SandboxAclArguments_GrantsRestrictedAppPackagesReadAndExecute()
    {
        var arguments = BrowserInstaller.SandboxAclArguments(@"C:\browsers\chromium-149");

        CollectionAssert.AreEqual(
            new[]
            {
                @"C:\browsers\chromium-149",
                "/grant",
                "*S-1-15-2-2:(OI)(CI)(RX)",
                "/q",
            },
            arguments);
    }

    [TestMethod]
    public void SandboxAclArguments_NamesTheDirectoryItWasGiven()
    {
        var arguments = BrowserInstaller.SandboxAclArguments(@"D:\a path with spaces\chromium");

        // The path is passed as its own argument rather than built into a command line, so a
        // directory with a space in it needs no quoting of ours and cannot be split.
        Assert.AreEqual(@"D:\a path with spaces\chromium", arguments[0]);
    }
}
