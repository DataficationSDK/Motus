using Motus.Abstractions;

namespace Motus.Tests.Browser;

[TestClass]
public class FirefoxArgsTests
{
    [TestMethod]
    public void Build_Headless_IncludesHeadlessFlag()
    {
        var options = new LaunchOptions { Headless = true };

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.Contains(args, "--headless");
    }

    [TestMethod]
    public void Build_Headed_OmitsHeadlessFlag()
    {
        var options = new LaunchOptions { Headless = false };

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.DoesNotContain(args, "--headless");
    }

    [TestMethod]
    public void Build_Headless_SetsEnvVar_MOZ_HEADLESS()
    {
        var options = new LaunchOptions { Headless = true };

        var (_, envVars) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsTrue(envVars.ContainsKey("MOZ_HEADLESS"));
        Assert.AreEqual("1", envVars["MOZ_HEADLESS"]);
    }

    [TestMethod]
    public void Build_Headed_DoesNotSetEnvVar_MOZ_HEADLESS()
    {
        var options = new LaunchOptions { Headless = false };

        var (_, envVars) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsFalse(envVars.ContainsKey("MOZ_HEADLESS"));
    }

    [TestMethod]
    public void Build_AlwaysIncludesDebuggingPort()
    {
        var options = new LaunchOptions();

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsTrue(args.Contains("--remote-debugging-port"));
        Assert.IsTrue(args.Contains("9222"));

        // Verify port follows the flag
        var portFlagIndex = args.IndexOf("--remote-debugging-port");
        Assert.AreEqual("9222", args[portFlagIndex + 1]);
    }

    [TestMethod]
    public void Build_AlwaysIncludesProfileFlag()
    {
        var options = new LaunchOptions();

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsTrue(args.Contains("-profile"));
        Assert.IsTrue(args.Contains("/tmp/profile"));

        var profileFlagIndex = args.IndexOf("-profile");
        Assert.AreEqual("/tmp/profile", args[profileFlagIndex + 1]);
    }

    [TestMethod]
    public void Build_IncludesDefaultArgs()
    {
        var options = new LaunchOptions();

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.Contains(args, "-no-remote");
        CollectionAssert.Contains(args, "-wait-for-browser");
        CollectionAssert.Contains(args, "--new-instance");
    }

    [TestMethod]
    public void Build_UserArgs_AreAppended()
    {
        var options = new LaunchOptions
        {
            Args = ["--custom-flag", "--another=value"]
        };

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.Contains(args, "--custom-flag");
        CollectionAssert.Contains(args, "--another=value");
    }

    [TestMethod]
    public void Build_IgnoreDefaultArgs_FiltersFirefoxDefaults()
    {
        var options = new LaunchOptions
        {
            IgnoreDefaultArgs = ["-no-remote", "-wait-for-browser"]
        };

        var (args, _) = FirefoxArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.DoesNotContain(args, "-no-remote");
        CollectionAssert.DoesNotContain(args, "-wait-for-browser");
        CollectionAssert.Contains(args, "--new-instance");
    }
}
