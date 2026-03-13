using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;

namespace Motus.Cli.Services;

public sealed class ProtocolUpdater
{
    private static readonly HttpClient Http = new();

    public async Task UpdateAsync(string? version, bool dryRun, string outputDir)
    {
        var npmVersion = version ?? "latest";
        Console.WriteLine($"Fetching devtools-protocol@{npmVersion} from npm...");

        var registryUrl = $"https://registry.npmjs.org/devtools-protocol/{npmVersion}";
        var metaJson = await Http.GetStringAsync(registryUrl);
        using var metaDoc = JsonDocument.Parse(metaJson);

        var tarballUrl = metaDoc.RootElement
            .GetProperty("dist")
            .GetProperty("tarball")
            .GetString()!;

        var resolvedVersion = metaDoc.RootElement.GetProperty("version").GetString()!;
        Console.WriteLine($"Downloading devtools-protocol@{resolvedVersion}...");

        using var tgzStream = await Http.GetStreamAsync(tarballUrl);
        using var gzStream = new GZipStream(tgzStream, CompressionMode.Decompress);

        string? browserProtocol = null;
        string? jsProtocol = null;

        using var tarReader = new TarReader(gzStream);
        while (await tarReader.GetNextEntryAsync() is { } entry)
        {
            var name = entry.Name.Replace('\\', '/');
            if (name.EndsWith("json/browser_protocol.json", StringComparison.Ordinal))
            {
                browserProtocol = await ReadEntryAsync(entry);
            }
            else if (name.EndsWith("json/js_protocol.json", StringComparison.Ordinal))
            {
                jsProtocol = await ReadEntryAsync(entry);
            }

            if (browserProtocol is not null && jsProtocol is not null)
                break;
        }

        if (browserProtocol is null || jsProtocol is null)
        {
            Console.Error.WriteLine("Failed to find protocol JSON files in the package.");
            return;
        }

        var browserOutPath = Path.Combine(outputDir, "browser_protocol.json");
        var jsOutPath = Path.Combine(outputDir, "js_protocol.json");

        PrintDiff("browser_protocol.json", browserOutPath, browserProtocol);
        PrintDiff("js_protocol.json", jsOutPath, jsProtocol);

        if (!dryRun)
        {
            Directory.CreateDirectory(outputDir);
            await File.WriteAllTextAsync(browserOutPath, browserProtocol);
            await File.WriteAllTextAsync(jsOutPath, jsProtocol);
            Console.WriteLine($"Protocol files written to {Path.GetFullPath(outputDir)}");
        }
        else
        {
            Console.WriteLine("Dry run complete. No files written.");
        }
    }

    private static void PrintDiff(string fileName, string existingPath, string newJson)
    {
        if (!File.Exists(existingPath))
        {
            Console.WriteLine($"  {fileName}: new file");
            return;
        }

        var existingJson = File.ReadAllText(existingPath);
        var diff = ProtocolDiffer.Compare(existingJson, newJson);

        if (!diff.HasChanges)
        {
            Console.WriteLine($"  {fileName}: no changes");
            return;
        }

        Console.WriteLine($"  {fileName}:");

        foreach (var d in diff.AddedDomains)
            Console.WriteLine($"    + domain {d}");
        foreach (var d in diff.RemovedDomains)
            Console.WriteLine($"    - domain {d}");
        foreach (var m in diff.ModifiedDomains)
        {
            Console.WriteLine($"    ~ {m.DomainName}");
            foreach (var c in m.AddedCommands)
                Console.WriteLine($"      + command {c}");
            foreach (var c in m.RemovedCommands)
                Console.WriteLine($"      - command {c}");
            foreach (var e in m.AddedEvents)
                Console.WriteLine($"      + event {e}");
            foreach (var e in m.RemovedEvents)
                Console.WriteLine($"      - event {e}");
        }
    }

    private static async Task<string> ReadEntryAsync(TarEntry entry)
    {
        if (entry.DataStream is null)
            return string.Empty;

        using var reader = new StreamReader(entry.DataStream, leaveOpen: true);
        return await reader.ReadToEndAsync();
    }
}
