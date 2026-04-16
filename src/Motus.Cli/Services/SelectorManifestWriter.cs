using System.Text.Json;
using Motus.Selectors;

namespace Motus.Cli.Services;

/// <summary>
/// Writes <see cref="SelectorManifest"/>s to disk as <c>*.selectors.json</c> sidecar files
/// next to the generated test or POM source file they describe.
/// </summary>
internal static class SelectorManifestWriter
{
    /// <summary>
    /// Derives the manifest sidecar path for a generated source file.
    /// <c>/tmp/LoginTest.cs</c> → <c>/tmp/LoginTest.selectors.json</c>.
    /// </summary>
    internal static string ManifestPathFor(string generatedFilePath)
    {
        if (string.IsNullOrWhiteSpace(generatedFilePath))
            throw new ArgumentException("Path must be non-empty.", nameof(generatedFilePath));

        var directory = Path.GetDirectoryName(generatedFilePath) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(generatedFilePath);

        // Strip a trailing ".g" for auto-generated files so "Foo.g.cs" -> "Foo.selectors.json".
        if (stem.EndsWith(".g", StringComparison.Ordinal))
            stem = stem[..^2];

        var fileName = $"{stem}.selectors.json";
        return directory.Length == 0 ? fileName : Path.Combine(directory, fileName);
    }

    /// <summary>
    /// Serializes <paramref name="manifest"/> as JSON and writes it to
    /// <paramref name="outputPath"/>, creating parent directories as needed.
    /// </summary>
    internal static async Task WriteAsync(
        SelectorManifest manifest, string outputPath, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(manifest, SelectorManifestJsonContext.Default.SelectorManifest);
        await File.WriteAllTextAsync(outputPath, json, ct).ConfigureAwait(false);
    }
}
