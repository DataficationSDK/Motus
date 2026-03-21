using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Motus.Cli.Services;

public sealed class BrowserInstaller
{
    private static readonly HttpClient Http = new();

    public async Task InstallAsync(string channel, string? revision, string? cachePathOverride)
    {
        if (channel.Equals("firefox", StringComparison.OrdinalIgnoreCase))
        {
            await InstallFirefoxAsync(cachePathOverride);
            return;
        }

        if (!channel.Equals("chromium", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{channel} should be installed system-wide. Use --channel chromium to download a standalone build.");
            return;
        }

        await InstallChromiumAsync(revision, cachePathOverride);
    }

    private async Task InstallChromiumAsync(string? revision, string? cachePathOverride)
    {
        var cachePath = cachePathOverride ?? DefaultCachePath();
        Directory.CreateDirectory(cachePath);

        Console.WriteLine("Querying latest stable Chromium build...");

        var json = await Http.GetStringAsync(
            "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json");
        using var doc = JsonDocument.Parse(json);

        var stable = doc.RootElement.GetProperty("channels").GetProperty("Stable");
        var version = revision ?? stable.GetProperty("version").GetString()!;

        var platformKey = GetPlatformKey();
        var downloads = stable.GetProperty("downloads").GetProperty("chrome");

        string? downloadUrl = null;
        foreach (var item in downloads.EnumerateArray())
        {
            if (item.GetProperty("platform").GetString() == platformKey)
            {
                downloadUrl = item.GetProperty("url").GetString();
                break;
            }
        }

        if (downloadUrl is null)
        {
            Console.Error.WriteLine($"No download found for platform: {platformKey}");
            return;
        }

        var destDir = Path.Combine(cachePath, $"chromium-{version}");
        if (Directory.Exists(destDir))
        {
            Console.WriteLine($"Chromium {version} already installed at {destDir}");
            WriteMarker(cachePath, "chromium", FindChromiumExecutable(destDir, platformKey));
            return;
        }

        Console.WriteLine($"Downloading Chromium {version} for {platformKey}...");
        var tempZip = Path.Combine(Path.GetTempPath(), $"chromium-{version}.zip");
        try
        {
            using (var stream = await Http.GetStreamAsync(downloadUrl))
            await using (var fs = File.Create(tempZip))
            {
                await stream.CopyToAsync(fs);
            }

            Console.WriteLine("Extracting...");
            ZipFile.ExtractToDirectory(tempZip, destDir, overwriteFiles: true);
        }
        finally
        {
            if (File.Exists(tempZip))
                File.Delete(tempZip);
        }

        var execPath = FindChromiumExecutable(destDir, platformKey);
        WriteMarker(cachePath, "chromium", execPath);

        SetExecutablePermissions(execPath);

        Console.WriteLine($"Chromium {version} installed at {execPath}");
    }

    private async Task InstallFirefoxAsync(string? cachePathOverride)
    {
        var cachePath = cachePathOverride ?? DefaultCachePath();
        Directory.CreateDirectory(cachePath);

        // Firefox automated download is supported on Windows (.zip available).
        // macOS (.dmg) and Linux (.tar.bz2) require platform tools to extract;
        // direct users to install via their system package manager.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Console.WriteLine("Automated Firefox download is not yet supported on this platform.");
            Console.WriteLine("Install Firefox using your system package manager:");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                Console.WriteLine("  brew install --cask firefox");
            else
                Console.WriteLine("  sudo apt install firefox  (or equivalent for your distribution)");
            Console.WriteLine("Or download from: https://www.mozilla.org/firefox/");
            return;
        }

        Console.WriteLine("Querying latest Firefox version...");

        var json = await Http.GetStringAsync(
            "https://product-details.mozilla.org/1.0/firefox_versions.json");
        using var doc = JsonDocument.Parse(json);

        var version = doc.RootElement.GetProperty("LATEST_FIREFOX_VERSION").GetString()!;

        var destDir = Path.Combine(cachePath, $"firefox-{version}");
        if (Directory.Exists(destDir))
        {
            Console.WriteLine($"Firefox {version} already installed at {destDir}");
            WriteMarker(cachePath, "firefox", FindFirefoxExecutable(destDir));
            return;
        }

        var osKey = Environment.Is64BitOperatingSystem ? "win64" : "win";
        var downloadUrl = $"https://download.mozilla.org/?product=firefox-{version}-SSL&os={osKey}&lang=en-US";

        Console.WriteLine($"Downloading Firefox {version} for {osKey}...");
        var tempFile = Path.Combine(Path.GetTempPath(), $"firefox-{version}.exe");
        try
        {
            using (var stream = await Http.GetStreamAsync(downloadUrl))
            await using (var fs = File.Create(tempFile))
            {
                await stream.CopyToAsync(fs);
            }

            // The Windows Firefox download is an installer executable.
            // Extract using the silent install option to the destination directory.
            Console.WriteLine("Installing...");
            Directory.CreateDirectory(destDir);

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = tempFile,
                ArgumentList = { "/S", $"/D={destDir}" },
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null)
                await proc.WaitForExitAsync();
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }

        var execPath = FindFirefoxExecutable(destDir);
        WriteMarker(cachePath, "firefox", execPath);

        Console.WriteLine($"Firefox {version} installed at {execPath}");
    }

    internal static string GetPlatformKey()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Environment.Is64BitOperatingSystem ? "win64" : "win32";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "mac-arm64" : "mac-x64";

        return "linux64";
    }

    internal static string DefaultCachePath() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".motus", "browsers");

    private static string FindChromiumExecutable(string destDir, string platformKey)
    {
        if (platformKey.StartsWith("mac", StringComparison.Ordinal))
        {
            var apps = Directory.GetDirectories(destDir, "*.app", SearchOption.AllDirectories);
            if (apps.Length > 0)
            {
                var macOs = Path.Combine(apps[0], "Contents", "MacOS");
                var bins = Directory.GetFiles(macOs);
                if (bins.Length > 0)
                    return bins[0];
            }
        }

        if (platformKey.StartsWith("win", StringComparison.Ordinal))
        {
            var exes = Directory.GetFiles(destDir, "chrome.exe", SearchOption.AllDirectories);
            if (exes.Length > 0)
                return exes[0];
        }

        var chromes = Directory.GetFiles(destDir, "chrome", SearchOption.AllDirectories);
        if (chromes.Length > 0)
            return chromes[0];

        var allFiles = Directory.GetFiles(destDir, "*", SearchOption.AllDirectories);
        return allFiles.FirstOrDefault() ?? destDir;
    }

    private static string FindFirefoxExecutable(string destDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var exes = Directory.GetFiles(destDir, "firefox.exe", SearchOption.AllDirectories);
            if (exes.Length > 0)
                return exes[0];
        }

        var bins = Directory.GetFiles(destDir, "firefox", SearchOption.AllDirectories);
        if (bins.Length > 0)
            return bins[0];

        var allFiles = Directory.GetFiles(destDir, "*", SearchOption.AllDirectories);
        return allFiles.FirstOrDefault() ?? destDir;
    }

    private static void WriteMarker(string cachePath, string channel, string executablePath)
    {
        // Write channel-specific marker
        var markerPath = Path.Combine(cachePath, $".installed.{channel}");
        File.WriteAllText(markerPath, executablePath);

        // Also write legacy marker for backward compatibility
        var legacyMarkerPath = Path.Combine(cachePath, ".installed");
        File.WriteAllText(legacyMarkerPath, executablePath);
    }

    private static void SetExecutablePermissions(string execPath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            File.SetUnixFileMode(execPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }
}
