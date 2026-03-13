using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Motus.Cli.Services;

public sealed class BrowserInstaller
{
    private static readonly HttpClient Http = new();

    public async Task InstallAsync(string channel, string? revision, string? cachePathOverride)
    {
        if (!channel.Equals("chromium", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"{channel} should be installed system-wide. Use --channel chromium to download a standalone build.");
            return;
        }

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
            WriteMarker(cachePath, FindExecutable(destDir, platformKey));
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

        var execPath = FindExecutable(destDir, platformKey);
        WriteMarker(cachePath, execPath);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            File.SetUnixFileMode(execPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }

        Console.WriteLine($"Chromium {version} installed at {execPath}");
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

    private static string FindExecutable(string destDir, string platformKey)
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

    private static void WriteMarker(string cachePath, string executablePath)
    {
        var markerPath = Path.Combine(cachePath, ".installed");
        File.WriteAllText(markerPath, executablePath);
    }
}
