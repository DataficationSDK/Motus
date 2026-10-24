using Motus.Abstractions;

namespace Motus.Tests.Browser;

/// <summary>
/// Launching with default options starts the browser <c>motus install</c> downloaded, which is what
/// a test run under <c>dotnet test</c> does. The download here is a script standing in for a
/// browser that records that it was started, so the test can tell it apart from any browser the
/// machine has without depending on one.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class InstalledBrowserLaunchTests
{
    [TestMethod]
    public async Task LaunchAsync_WithDefaultOptions_StartsTheDownloadedBrowser()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The stand-in browser is a shell script.");
            return;
        }

        // An executable path from the environment or motus.json outranks the download, as it
        // should, and the configuration is read once per process, so it cannot be cleared here.
        if (MotusConfigLoader.Config.Launch?.ExecutablePath is { Length: > 0 } configured)
        {
            Assert.Inconclusive($"An executable path is configured ({configured}), and it outranks the download.");
            return;
        }

        var root = Path.Combine(Path.GetTempPath(), $"motus-install-{Guid.NewGuid():N}");
        var originalRoot = BrowserFinder.InstallRoot;
        try
        {
            var folder = Path.Combine(root, "chromium-149.0.7827.54", "chrome-linux64");
            Directory.CreateDirectory(folder);

            var started = Path.Combine(root, "started");
            var executable = Path.Combine(folder, "chrome");
            File.WriteAllText(executable, $"#!/bin/sh\necho started > '{started}'\nexit 1\n");
            File.SetUnixFileMode(executable,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(InstalledBrowsers.MarkerPath(root, "chromium"), executable);

            BrowserFinder.InstallRoot = root;

            // The stand-in exits rather than speaking CDP, so the launch fails. What matters is
            // which executable it started.
            try
            {
                await using var browser = await MotusLauncher.LaunchAsync(
                    new LaunchOptions { Headless = true, Timeout = 5_000 });
                Assert.Fail("The stand-in browser cannot be driven, so the launch should not have succeeded.");
            }
            catch (Exception ex) when (ex is not AssertFailedException)
            {
            }

            Assert.IsTrue(File.Exists(started),
                "LaunchAsync with default options should start the browser motus install recorded.");
        }
        finally
        {
            BrowserFinder.InstallRoot = originalRoot;
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
