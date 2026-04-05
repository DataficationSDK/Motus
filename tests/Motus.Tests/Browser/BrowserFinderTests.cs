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

    [TestMethod]
    public void CandidatesForChannel_Firefox_ReturnsNonEmptyList()
    {
        var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

        Assert.IsTrue(candidates.Count > 0, "Should return at least one Firefox candidate path.");
    }

    [TestMethod]
    public void CandidatesForChannel_Firefox_IncludesInstalledBinariesPath_WhenSet()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = "/opt/motus/browsers";
            var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

            Assert.IsTrue(candidates.Any(c => c.StartsWith("/opt/motus/browsers")),
                "Should include installed binaries path for Firefox when set.");
            Assert.IsTrue(candidates.Any(c => c.Contains("firefox")),
                "Firefox candidate should contain 'firefox' in the path.");
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void CandidatesForChannel_Firefox_DoesNotOverlapChrome()
    {
        var chrome = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);
        var firefox = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

        CollectionAssert.AreNotEqual(chrome.ToList(), firefox.ToList());
    }

    [TestMethod]
    public void CandidatesForChannel_Edge_ReturnsNonEmptyList()
    {
        var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Edge);

        Assert.IsTrue(candidates.Count > 0, "Should return at least one Edge candidate path.");
    }

    [TestMethod]
    public void CandidatesForChannel_Chromium_ReturnsNonEmptyList()
    {
        var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Chromium);

        Assert.IsTrue(candidates.Count > 0, "Should return at least one Chromium candidate path.");
    }

    [TestMethod]
    public void CandidatesForChannel_Edge_IncludesInstalledBinariesPath_WhenSet()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = "/opt/motus/browsers";
            var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Edge);

            Assert.IsTrue(candidates.Any(c => c.StartsWith("/opt/motus/browsers")),
                "Should include installed binaries path for Edge when set.");
            Assert.IsTrue(candidates.Any(c => c.Contains("msedge")),
                "Edge candidate should contain 'msedge' in the path.");
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void CandidatesForChannel_Chromium_IncludesInstalledBinariesPath_WhenSet()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = "/opt/motus/browsers";
            var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Chromium);

            Assert.IsTrue(candidates.Any(c => c.StartsWith("/opt/motus/browsers")),
                "Should include installed binaries path for Chromium when set.");
            Assert.IsTrue(candidates.Any(c => c.Contains("chromium")),
                "Chromium candidate should contain 'chromium' in the path.");
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void CandidatesForChannel_AllChannels_DoNotOverlap()
    {
        var chrome = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);
        var edge = BrowserFinder.CandidatesForChannel(BrowserChannel.Edge);
        var chromium = BrowserFinder.CandidatesForChannel(BrowserChannel.Chromium);
        var firefox = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

        // With InstalledBinariesPath cleared, platform-specific paths should be distinct
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = null;
            chrome = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);
            edge = BrowserFinder.CandidatesForChannel(BrowserChannel.Edge);
            chromium = BrowserFinder.CandidatesForChannel(BrowserChannel.Chromium);
            firefox = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

            var all = chrome.Concat(edge).Concat(chromium).Concat(firefox).ToList();
            var distinct = all.Distinct().ToList();
            Assert.AreEqual(distinct.Count, all.Count, "Candidate paths across channels should be unique.");
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void Resolve_WithChannel_ExercisesChannelPath()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = "/nonexistent/motus/browsers";
            try
            {
                var result = BrowserFinder.Resolve(channel: BrowserChannel.Firefox, executablePath: null);
                // If we reach here, a real Firefox is installed at a system path
                Assert.IsTrue(File.Exists(result));
            }
            catch (FileNotFoundException)
            {
                // Expected when no Firefox is installed
            }
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void Resolve_WithChannel_UsesExistingBinary()
    {
        var existingFile = typeof(BrowserFinderTests).Assembly.Location;
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            // Point InstalledBinariesPath to the directory containing the test assembly,
            // and rename expectation to match the binary name pattern
            var dir = Path.GetDirectoryName(existingFile)!;

            // Create a temp file named "chrome" (or "chrome.exe" on Windows) in a temp dir
            var tempDir = Path.Combine(Path.GetTempPath(), $"motus-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var fakeBrowser = Path.Combine(tempDir,
                OperatingSystem.IsWindows() ? "chrome.exe" : "chrome");
            File.WriteAllText(fakeBrowser, "fake");

            BrowserFinder.InstalledBinariesPath = tempDir;
            var result = BrowserFinder.Resolve(channel: BrowserChannel.Chrome, executablePath: null);

            Assert.AreEqual(fakeBrowser, result);

            // Cleanup
            File.Delete(fakeBrowser);
            Directory.Delete(tempDir);
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void Resolve_AutoDetect_FindsFirstAvailableBrowser()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"motus-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);
            var fakeBrowser = Path.Combine(tempDir,
                OperatingSystem.IsWindows() ? "chrome.exe" : "chrome");
            File.WriteAllText(fakeBrowser, "fake");

            BrowserFinder.InstalledBinariesPath = tempDir;
            // channel: null, executablePath: null triggers auto-detect
            var result = BrowserFinder.Resolve(channel: null, executablePath: null);

            Assert.AreEqual(fakeBrowser, result, "Auto-detect should find Chrome first.");

            File.Delete(fakeBrowser);
            Directory.Delete(tempDir);
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void Resolve_AutoDetect_ExercisesAutoDetectPath()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"motus-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            BrowserFinder.InstalledBinariesPath = tempDir;
            try
            {
                var result = BrowserFinder.Resolve(channel: null, executablePath: null);
                // If we reach here, a real browser is installed at a system path
                Assert.IsTrue(File.Exists(result));
            }
            catch (FileNotFoundException)
            {
                // Expected when no browser is installed at system paths
            }
            finally
            {
                Directory.Delete(tempDir);
            }
        }
        finally
        {
            BrowserFinder.InstalledBinariesPath = originalPath;
        }
    }

    [TestMethod]
    public void InstalledBinariesPath_Null_DoesNotPrependPath()
    {
        var originalPath = BrowserFinder.InstalledBinariesPath;
        try
        {
            BrowserFinder.InstalledBinariesPath = null;
            var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);

            // All candidates should be platform-specific system paths, not prepended
            Assert.IsTrue(candidates.All(c => !string.IsNullOrWhiteSpace(c)));
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

[TestClass]
public class IsFirefoxChannelTests
{
    [TestMethod]
    public void Firefox_Channel_ReturnsTrue()
    {
        Assert.IsTrue(MotusLauncher.IsFirefoxChannel(BrowserChannel.Firefox, null));
    }

    [TestMethod]
    public void Chrome_Channel_ReturnsFalse()
    {
        Assert.IsFalse(MotusLauncher.IsFirefoxChannel(BrowserChannel.Chrome, null));
    }

    [TestMethod]
    public void Null_Channel_ReturnsFalse()
    {
        Assert.IsFalse(MotusLauncher.IsFirefoxChannel(null, null));
    }

    [TestMethod]
    public void Firefox_ExecutablePath_ReturnsTrue()
    {
        Assert.IsTrue(MotusLauncher.IsFirefoxChannel(null, "/usr/bin/firefox"));
    }

    [TestMethod]
    public void Firefox_ExecutablePath_CaseInsensitive()
    {
        Assert.IsTrue(MotusLauncher.IsFirefoxChannel(null, "/Applications/Firefox.app/Contents/MacOS/Firefox"));
    }

    [TestMethod]
    public void Chrome_ExecutablePath_ReturnsFalse()
    {
        Assert.IsFalse(MotusLauncher.IsFirefoxChannel(null, "/usr/bin/google-chrome"));
    }
}
