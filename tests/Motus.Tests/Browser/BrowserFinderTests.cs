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
    public void CandidatesForChannel_Firefox_ReturnsNonEmptyList()
    {
        var candidates = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

        Assert.IsTrue(candidates.Count > 0, "Should return at least one Firefox candidate path.");
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
    public void CandidatesForChannel_AllChannels_DoNotOverlap()
    {
        var chrome = BrowserFinder.CandidatesForChannel(BrowserChannel.Chrome);
        var edge = BrowserFinder.CandidatesForChannel(BrowserChannel.Edge);
        var chromium = BrowserFinder.CandidatesForChannel(BrowserChannel.Chromium);
        var firefox = BrowserFinder.CandidatesForChannel(BrowserChannel.Firefox);

        var all = chrome.Concat(edge).Concat(chromium).Concat(firefox).ToList();
        var distinct = all.Distinct().ToList();
        Assert.AreEqual(distinct.Count, all.Count, "Candidate paths across channels should be unique.");
    }

    [TestMethod]
    public void Resolve_WithChannel_ExercisesChannelPath()
    {
        using var machine = new FakeMachine();
        var originalRoot = BrowserFinder.InstallRoot;
        try
        {
            BrowserFinder.InstallRoot = machine.InstallRoot;
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
            BrowserFinder.InstallRoot = originalRoot;
        }
    }

    [TestMethod]
    public void Resolve_AutoDetect_ExercisesAutoDetectPath()
    {
        using var machine = new FakeMachine();
        var originalRoot = BrowserFinder.InstallRoot;
        try
        {
            BrowserFinder.InstallRoot = machine.InstallRoot;
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
        }
        finally
        {
            BrowserFinder.InstallRoot = originalRoot;
        }
    }

    [TestMethod]
    public void Resolve_WithChannel_UsesExistingSystemBinary()
    {
        using var machine = new FakeMachine();
        var chrome = machine.AddSystemBrowser(BrowserChannel.Chrome);

        var result = machine.Resolve(BrowserChannel.Chrome);

        Assert.AreEqual(chrome, result);
    }

    [TestMethod]
    public void Resolve_AutoDetect_TriesChromeThenEdgeThenChromium()
    {
        using var machine = new FakeMachine();
        var chromium = machine.AddSystemBrowser(BrowserChannel.Chromium);

        Assert.AreEqual(chromium, machine.Resolve(channel: null));

        var edge = machine.AddSystemBrowser(BrowserChannel.Edge);

        Assert.AreEqual(edge, machine.Resolve(channel: null), "Edge comes before Chromium.");

        var chrome = machine.AddSystemBrowser(BrowserChannel.Chrome);

        Assert.AreEqual(chrome, machine.Resolve(channel: null), "Chrome comes before Edge.");
    }

    // The case the default path exists for: a machine with nothing but what motus install put
    // there, such as a fresh CI image, used to report that no browser could be found at all.
    [TestMethod]
    public void Resolve_AutoDetect_FindsTheDownload_WhenTheMachineHasNoBrowser()
    {
        using var machine = new FakeMachine();
        var downloaded = machine.Install(BrowserChannel.Chromium);

        Assert.AreEqual(downloaded, machine.Resolve(channel: null));
    }

    [TestMethod]
    public void Resolve_AutoDetect_PrefersTheDownload_OverASystemBrowser()
    {
        using var machine = new FakeMachine();
        machine.AddSystemBrowser(BrowserChannel.Chrome);
        machine.AddSystemBrowser(BrowserChannel.Edge);
        machine.AddSystemBrowser(BrowserChannel.Chromium);
        var downloaded = machine.Install(BrowserChannel.Chromium);

        Assert.AreEqual(downloaded, machine.Resolve(channel: null),
            "The browser the user chose to install should win over whatever the machine happens to have.");
    }

    [TestMethod]
    public void Resolve_AutoDetect_FallsBackToTheSystem_WhenThereIsNoMarker()
    {
        using var machine = new FakeMachine();
        var chrome = machine.AddSystemBrowser(BrowserChannel.Chrome);

        // A download with no marker beside it is not something the installer leaves behind, and
        // is not guessed at: the marker is what names the executable.
        Directory.CreateDirectory(Path.Combine(machine.InstallRoot, "chromium-149.0.7827.54", "chrome-linux64"));
        File.WriteAllText(Path.Combine(machine.InstallRoot, "chromium-149.0.7827.54", "chrome-linux64", "chrome"), "");

        Assert.AreEqual(chrome, machine.Resolve(channel: null));
    }

    [TestMethod]
    public void Resolve_AutoDetect_FallsBackToTheSystem_WhenTheMarkerIsStale()
    {
        using var machine = new FakeMachine();
        var chrome = machine.AddSystemBrowser(BrowserChannel.Chrome);
        var downloaded = machine.Install(BrowserChannel.Chromium);

        // The cache cleaned by hand, leaving the marker naming a folder that is gone.
        Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(downloaded))!, recursive: true);

        Assert.AreEqual(chrome, machine.Resolve(channel: null));
    }

    [TestMethod]
    public void Resolve_AutoDetect_Throws_WhenTheMarkerIsStaleAndTheMachineHasNoBrowser()
    {
        using var machine = new FakeMachine();
        var downloaded = machine.Install(BrowserChannel.Chromium);
        File.Delete(downloaded);

        var ex = Assert.ThrowsException<FileNotFoundException>(() => machine.Resolve(channel: null));
        StringAssert.Contains(ex.Message, "motus install");
    }

    [TestMethod]
    public void Resolve_AutoDetect_IgnoresAnEmptyMarker()
    {
        using var machine = new FakeMachine();
        var chrome = machine.AddSystemBrowser(BrowserChannel.Chrome);
        Directory.CreateDirectory(machine.InstallRoot);
        File.WriteAllText(InstalledBrowsers.MarkerPath(machine.InstallRoot, "chromium"), "   \n");

        Assert.AreEqual(chrome, machine.Resolve(channel: null));
    }

    [TestMethod]
    public void Resolve_ReadsAMarkerWithATrailingNewline()
    {
        using var machine = new FakeMachine();
        var downloaded = machine.Install(BrowserChannel.Chromium);
        File.WriteAllText(InstalledBrowsers.MarkerPath(machine.InstallRoot, "chromium"), downloaded + Environment.NewLine);

        Assert.AreEqual(downloaded, machine.Resolve(channel: null));
    }

    [TestMethod]
    public void Resolve_ExplicitPath_WinsOverTheDownload()
    {
        using var machine = new FakeMachine();
        machine.Install(BrowserChannel.Chromium);
        var pinned = machine.AddFile("pinned", "chrome");

        Assert.AreEqual(pinned, machine.Resolve(channel: null, executablePath: pinned));
        Assert.AreEqual(pinned, machine.Resolve(BrowserChannel.Chromium, executablePath: pinned));
    }

    [TestMethod]
    public void Resolve_ChromiumChannel_PrefersTheDownload_OverASystemChromium()
    {
        using var machine = new FakeMachine();
        machine.AddSystemBrowser(BrowserChannel.Chromium);
        var downloaded = machine.Install(BrowserChannel.Chromium);

        Assert.AreEqual(downloaded, machine.Resolve(BrowserChannel.Chromium));
    }

    [TestMethod]
    public void Resolve_ChromiumChannel_FallsBackToTheSystem_WhenTheMarkerIsStale()
    {
        using var machine = new FakeMachine();
        var chromium = machine.AddSystemBrowser(BrowserChannel.Chromium);
        File.Delete(machine.Install(BrowserChannel.Chromium));

        Assert.AreEqual(chromium, machine.Resolve(BrowserChannel.Chromium));
    }

    // Chrome and Edge name the branded browsers. The download is a Chromium build, so asking for
    // Chrome by name must not quietly hand back something else, with or without Chrome present.
    [TestMethod]
    [DataRow(BrowserChannel.Chrome)]
    [DataRow(BrowserChannel.Edge)]
    public void Resolve_BrandedChannel_IgnoresTheChromiumDownload(BrowserChannel channel)
    {
        using var machine = new FakeMachine();
        machine.Install(BrowserChannel.Chromium);

        Assert.ThrowsException<FileNotFoundException>(() => machine.Resolve(channel));

        var branded = machine.AddSystemBrowser(channel);

        Assert.AreEqual(branded, machine.Resolve(channel));
    }

    [TestMethod]
    public void Resolve_FirefoxChannel_PrefersTheFirefoxDownload()
    {
        using var machine = new FakeMachine();
        machine.AddSystemBrowser(BrowserChannel.Firefox);
        machine.Install(BrowserChannel.Chromium);
        var downloaded = machine.Install(BrowserChannel.Firefox);

        Assert.AreEqual(downloaded, machine.Resolve(BrowserChannel.Firefox));
    }

    [TestMethod]
    public void Resolve_FirefoxChannel_DoesNotTakeTheChromiumDownload()
    {
        using var machine = new FakeMachine();
        machine.Install(BrowserChannel.Chromium);

        Assert.ThrowsException<FileNotFoundException>(() => machine.Resolve(BrowserChannel.Firefox));
    }

    // Auto-detect has never picked Firefox, which is driven over a different protocol, and a
    // Firefox download does not change that.
    [TestMethod]
    public void Resolve_AutoDetect_DoesNotTakeAFirefoxDownload()
    {
        using var machine = new FakeMachine();
        machine.Install(BrowserChannel.Firefox);

        Assert.ThrowsException<FileNotFoundException>(() => machine.Resolve(channel: null));
    }

    [TestMethod]
    public void Resolve_WithoutARoot_ReadsBrowserFinderInstallRoot()
    {
        using var machine = new FakeMachine();
        var downloaded = machine.Install(BrowserChannel.Chromium);
        var originalRoot = BrowserFinder.InstallRoot;
        try
        {
            BrowserFinder.InstallRoot = machine.InstallRoot;

            Assert.AreEqual(downloaded, BrowserFinder.Resolve(BrowserChannel.Chromium, executablePath: null));
        }
        finally
        {
            BrowserFinder.InstallRoot = originalRoot;
        }
    }

    [TestMethod]
    public void InstallRoot_DefaultsToTheInstallersCache()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".motus", "browsers");

        Assert.AreEqual(expected, InstalledBrowsers.DefaultRoot);
        Assert.AreEqual(Path.Combine(expected, ".installed.chromium"), InstalledBrowsers.MarkerPath(expected, "Chromium"));
    }

    /// <summary>
    /// A browser cache and a set of system locations in a temporary folder, so that what is
    /// installed on the machine running the tests cannot change what they find.
    /// </summary>
    private sealed class FakeMachine : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), $"motus-finder-{Guid.NewGuid():N}");

        public string InstallRoot => Path.Combine(_dir, "browsers");

        /// <summary>
        /// Lays a download out the way motus install does, a versioned folder holding the
        /// platform's archive folder, and writes the marker that names its executable.
        /// </summary>
        public string Install(BrowserChannel channel)
        {
            var name = channel.ToString().ToLowerInvariant();
            var executable = channel == BrowserChannel.Firefox
                ? AddFile(Path.Combine("browsers", $"{name}-150.0"), "firefox")
                : AddFile(Path.Combine("browsers", $"{name}-149.0.7827.54", "chrome-linux64"), "chrome");
            File.WriteAllText(InstalledBrowsers.MarkerPath(InstallRoot, name), executable);
            return executable;
        }

        public string AddSystemBrowser(BrowserChannel channel) =>
            AddFile(Path.Combine("system", channel.ToString()), "browser");

        public string AddFile(string folder, string name)
        {
            var dir = Path.Combine(_dir, folder);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, name);
            File.WriteAllText(path, "");
            return path;
        }

        public string Resolve(BrowserChannel? channel, string? executablePath = null) =>
            BrowserFinder.Resolve(channel, executablePath, InstallRoot, SystemCandidates);

        // Each channel's system location is listed whether or not it exists yet, the way the
        // real candidate lists name paths that may not be installed.
        private IReadOnlyList<string> SystemCandidates(BrowserChannel channel) =>
            [Path.Combine(_dir, "system", channel.ToString(), "browser")];

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir))
                    Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
            }
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

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

        CollectionAssert.Contains(args, "--headless=new");
    }

    [TestMethod]
    public void Build_Headed_OmitsHeadlessArg()
    {
        var options = new LaunchOptions { Headless = false };

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

        CollectionAssert.DoesNotContain(args, "--headless=new");
        CollectionAssert.Contains(args, "--disable-blink-features=AutomationControlled");
    }

    [TestMethod]
    public void Build_WithAPort_OpensTheDebuggingPort()
    {
        var options = new LaunchOptions();

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

        Assert.IsTrue(args.Any(a => a == "--remote-debugging-port=9222"));
        CollectionAssert.DoesNotContain(args, "--remote-debugging-pipe");
    }

    [TestMethod]
    public void Build_WithoutAPort_AsksForThePipeInstead()
    {
        var options = new LaunchOptions();

        var args = ChromiumArgs.Build(options, "/tmp/profile");

        CollectionAssert.Contains(args, "--remote-debugging-pipe");
        Assert.IsFalse(
            args.Any(a => a.StartsWith("--remote-debugging-port", StringComparison.Ordinal)),
            "a browser driven over a pipe should not also be opening a port");
    }

    [TestMethod]
    public void Build_AlwaysIncludesUserDataDir()
    {
        var options = new LaunchOptions();

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

        Assert.IsTrue(args.Any(a => a == "--user-data-dir=/tmp/profile"));
    }

    [TestMethod]
    public void Build_IgnoreDefaultArgs_FiltersSpecifiedDefaults()
    {
        var options = new LaunchOptions
        {
            IgnoreDefaultArgs = ["--disable-sync", "--no-first-run"]
        };

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

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

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

        CollectionAssert.Contains(args, "--custom-flag");
        CollectionAssert.Contains(args, "--another=value");
    }

    [TestMethod]
    public void Build_DownloadsPath_AppendsArg()
    {
        var options = new LaunchOptions { DownloadsPath = "/tmp/downloads" };

        var args = ChromiumArgs.Build(options, "/tmp/profile", 9222);

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
