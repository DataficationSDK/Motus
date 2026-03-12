using Motus.Abstractions;

namespace Motus.Tests.Browser;

[TestClass]
public class BrowserFinderTests
{
    [TestMethod]
    public void Resolve_WithExistingExecutablePath_ReturnsThatPath()
    {
        // Use the test assembly's own DLL as a known-existing file
        var existingFile = typeof(BrowserFinderTests).Assembly.Location;

        var result = BrowserFinder.Resolve(channel: null, executablePath: existingFile);

        Assert.AreEqual(existingFile, result);
    }

    [TestMethod]
    [ExpectedException(typeof(FileNotFoundException))]
    public void Resolve_WithNonExistentExecutablePath_ThrowsFileNotFoundException()
    {
        BrowserFinder.Resolve(channel: null, executablePath: "/nonexistent/path/to/browser");
    }

    [TestMethod]
    public void CandidatesForChannel_ReturnsNonEmptyList()
    {
        var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);

        Assert.IsTrue(candidates.Count > 0, "Should return at least one candidate path.");
    }

    [TestMethod]
    public void CandidatesForChannel_ReturnsDifferentPathsPerChannel()
    {
        var chrome = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);
        var edge = BrowserFinder.CandidatesForChannel(BrowserChannel.Edge);

        CollectionAssert.AreNotEqual(chrome.ToList(), edge.ToList());
    }

    [TestMethod]
    public void CandidatesForChannel_IncludesInstalledBinariesPath_WhenSet()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = "/opt/motus/browsers";
            var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);

            Assert.IsTrue(candidates.Any(c => c.StartsWith("/opt/motus/browsers")),
                "Should include installed binaries path when set.");
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }
}

[TestClass]
public class ChromiumArgsTests
{
    [TestMethod]
    public void Build_Headless_IncludesHeadlessArg()
    {
        var options = new LaunchOptions { Headless = true };

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.Contains(args, "--headless=new");
    }

    [TestMethod]
    public void Build_Headed_OmitsHeadlessArg()
    {
        var options = new LaunchOptions { Headless = false };

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.DoesNotContain(args, "--headless=new");
        CollectionAssert.Contains(args, "--disable-blink-features=AutomationControlled");
    }

    [TestMethod]
    public void Build_AlwaysIncludesDebuggingPort()
    {
        var options = new LaunchOptions();

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsTrue(args.Any(a => a == "--remote-debugging-port=9222"));
    }

    [TestMethod]
    public void Build_AlwaysIncludesUserDataDir()
    {
        var options = new LaunchOptions();

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsTrue(args.Any(a => a == "--user-data-dir=/tmp/profile"));
    }

    [TestMethod]
    public void Build_IgnoreDefaultArgs_FiltersSpecifiedDefaults()
    {
        var options = new LaunchOptions
        {
            IgnoreDefaultArgs = ["--disable-sync", "--no-first-run"]
        };

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.DoesNotContain(args, "--disable-sync");
        CollectionAssert.DoesNotContain(args, "--no-first-run");
        CollectionAssert.Contains(args, "--disable-extensions");
    }

    [TestMethod]
    public void Build_UserArgs_AreAppended()
    {
        var options = new LaunchOptions
        {
            Args = ["--custom-flag", "--another=value"]
        };

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        CollectionAssert.Contains(args, "--custom-flag");
        CollectionAssert.Contains(args, "--another=value");
    }

    [TestMethod]
    public void Build_DownloadsPath_AppendsArg()
    {
        var options = new LaunchOptions { DownloadsPath = "/tmp/downloads" };

        var args = ChromiumArgs.Build(options, 9222, "/tmp/profile");

        Assert.IsTrue(args.Any(a => a.Contains("/tmp/downloads")));
    }
}
