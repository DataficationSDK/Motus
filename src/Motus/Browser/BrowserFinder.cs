using System.Runtime.InteropServices;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Discovers browser executables by channel or explicit path.
/// </summary>
internal static class BrowserFinder
{
    /// <summary>
    /// The browser cache searched for a <c>motus install</c> download. It is the installer's default
    /// cache, and tests point it at a folder of their own so that what is installed on the machine
    /// running them cannot change their outcome.
    /// </summary>
    internal static string InstallRoot { get; set; } = InstalledBrowsers.DefaultRoot;

    /// <summary>
    /// Resolves a browser executable path from channel preference or explicit path.
    /// </summary>
    internal static string Resolve(BrowserChannel? channel, string? executablePath) =>
        Resolve(channel, executablePath, InstallRoot, CandidatesForChannel);

    /// <summary>
    /// Resolves a browser executable path, with the download cache and the system locations
    /// supplied by the caller.
    /// </summary>
    /// <remarks>
    /// An explicit path wins, then a browser <c>motus install</c> downloaded, then one the machine
    /// has installed. The download comes before the system browser because it is the one the user
    /// chose to install, usually pinned to a version, and a suite that found a system Chrome first
    /// would quietly test whatever Chrome the machine happened to have.
    ///
    /// A named channel is binding, and only a download recorded for that same channel answers it.
    /// <c>motus install</c> downloads Chromium and Firefox; Chrome and Edge are branded browsers it
    /// leaves to their own installers, so those channels still resolve to the installed browser.
    /// With no channel, the Chromium download is tried before the system Chrome, Edge and Chromium,
    /// in that order. A Firefox download is never picked without the Firefox channel, just as a
    /// system Firefox is not.
    /// </remarks>
    internal static string Resolve(
        BrowserChannel? channel,
        string? executablePath,
        string installRoot,
        Func<BrowserChannel, IReadOnlyList<string>> systemCandidates)
    {
        if (executablePath is not null)
        {
            if (!File.Exists(executablePath))
                throw new FileNotFoundException($"Browser executable not found: {executablePath}", executablePath);
            return executablePath;
        }

        if (channel is not null)
        {
            return InstalledBrowsers.Find(installRoot, channel.Value)
                   ?? FindFirstExisting(systemCandidates(channel.Value))
                   ?? throw new FileNotFoundException($"No {channel} installation found.");
        }

        if (InstalledBrowsers.Find(installRoot, BrowserChannel.Chromium) is { } downloaded)
            return downloaded;

        // Auto-detect: try Chrome, Edge, Chromium in order
        foreach (var ch in new[] { BrowserChannel.Chrome, BrowserChannel.Edge, BrowserChannel.Chromium })
        {
            var path = FindFirstExisting(systemCandidates(ch));
            if (path is not null)
                return path;
        }

        throw new FileNotFoundException(
            "No supported browser found. Install Chrome, Edge, or Chromium, or run 'motus install' to download Chromium.");
    }

    internal static IReadOnlyList<string> CandidatesForChannel(BrowserChannel channel)
    {
        var candidates = new List<string>();

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
