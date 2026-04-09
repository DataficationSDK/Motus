using System.Runtime.InteropServices;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Discovers browser executables by channel or explicit path.
/// </summary>
internal static class BrowserFinder
{
    /// <summary>
    /// When set, this path is prepended to candidate lists for all channels.
    /// Used by the install system to register downloaded binaries.
    /// </summary>
    internal static string? InstalledBinariesPath { get; set; }

    /// <summary>
    /// Resolves a browser executable path from channel preference or explicit path.
    /// </summary>
    internal static string Resolve(BrowserChannel? channel, string? executablePath)
    {
        if (executablePath is not null)
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException($"Browser executable not found: {executablePath}", executablePath);
            return executablePath;
        }

        if (channel is not null)
        {
            return FindFirstExisting(CandidatesForChannel(channel.Value))
                   ?? throw new FileNotFoundException($"No {channel} installation found.");
        }

        // Auto-detect: try Chrome, Edge, Chromium in order
        foreach (var ch in new[] { BrowserChannel.Chrome, BrowserChannel.Edge, BrowserChannel.Chromium })
        {
            var path = FindFirstExisting(CandidatesForChannel(ch));
            if (path is not null)
                return path;
        }

        throw new FileNotFoundException("No supported browser found. Install Chrome, Edge, or Chromium.");
    }

    internal static IReadOnlyList<string> CandidatesForChannel(BrowserChannel channel)
    {
        var candidates = new List<string>();

        if (InstalledBinariesPath is not null)
        {
            var name = channel switch
            {
                BrowserChannel.Chrome => "chrome",
                BrowserChannel.Edge => "msedge",
                BrowserChannel.Chromium => "chromium",
                BrowserChannel.Firefox => "firefox",
                _ => "chrome"
            };

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                candidates.Add(Path.Combine(InstalledBinariesPath, $"{name}.exe"));
            else
                candidates.Add(Path.Combine(InstalledBinariesPath, name));
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            candidates.AddRange(channel switch
            {
                BrowserChannel.Chrome => new[]
                {
                    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
                },
                BrowserChannel.Edge => new[]
                {
                    "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge"
                },
                BrowserChannel.Chromium => new[]
                {
                    "/Applications/Chromium.app/Contents/MacOS/Chromium"
                },
                BrowserChannel.Firefox => new[]
                {
                    "/Applications/Firefox.app/Contents/MacOS/firefox",
                    "/Applications/Firefox Nightly.app/Contents/MacOS/firefox"
                },
                _ => Array.Empty<string>()
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            candidates.AddRange(channel switch
            {
                BrowserChannel.Chrome => new[]
                {
                    "/usr/bin/google-chrome-stable",
                    "/usr/bin/google-chrome"
                },
                BrowserChannel.Edge => new[]
                {
                    "/usr/bin/microsoft-edge-stable",
                    "/usr/bin/microsoft-edge"
                },
                BrowserChannel.Chromium => new[]
                {
                    "/usr/bin/chromium-browser",
                    "/usr/bin/chromium"
                },
                BrowserChannel.Firefox => new[]
                {
                    "/usr/bin/firefox",
                    "/usr/bin/firefox-esr",
                    "/usr/local/bin/firefox",
                    "/snap/bin/firefox"
                },
                _ => Array.Empty<string>()
            });
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            candidates.AddRange(channel switch
            {
                BrowserChannel.Chrome => new[]
                {
                    Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                    Path.Combine(localAppData, "Google", "Chrome", "Application", "chrome.exe")
                },
                BrowserChannel.Edge => new[]
                {
                    Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                    Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe")
                },
                BrowserChannel.Chromium => new[]
                {
                    Path.Combine(localAppData, "Chromium", "Application", "chrome.exe")
                },
                BrowserChannel.Firefox => new[]
                {
                    Path.Combine(programFiles, "Mozilla Firefox", "firefox.exe"),
                    Path.Combine(programFilesX86, "Mozilla Firefox", "firefox.exe")
                },
                _ => Array.Empty<string>()
            });
        }

        return candidates;
    }

    private static string? FindFirstExisting(IReadOnlyList<string> candidates)
    {
        foreach (var path in candidates)
        {
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
