using System.Text.Json;
using Motus.Cli.Services;
using Motus.Selectors;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class SelectorManifestWriterTests
{
    [TestMethod]
    public void ManifestPathFor_ReplacesCsExtensionWithSelectorsJson()
    {
        var tempDir = Path.GetTempPath();
        var input = Path.Combine(tempDir, "LoginTest.cs");

        var path = SelectorManifestWriter.ManifestPathFor(input);

        Assert.AreEqual("LoginTest.selectors.json", Path.GetFileName(path));
        Assert.AreEqual(
            Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(Path.GetDirectoryName(path)!).TrimEnd(Path.DirectorySeparatorChar));
    }

    [TestMethod]
    public void ManifestPathFor_StripsDotGSuffix()
    {
        var tempDir = Path.GetTempPath();
        var input = Path.Combine(tempDir, "LoginPage.g.cs");

        var path = SelectorManifestWriter.ManifestPathFor(input);

        Assert.AreEqual("LoginPage.selectors.json", Path.GetFileName(path));
        Assert.AreEqual(
            Path.GetFullPath(tempDir).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(Path.GetDirectoryName(path)!).TrimEnd(Path.DirectorySeparatorChar));
    }

    [TestMethod]
    public void ManifestPathFor_NoDirectory_ReturnsBareFilename()
    {
        var path = SelectorManifestWriter.ManifestPathFor("RecordedTest.cs");
        Assert.AreEqual("RecordedTest.selectors.json", path);
    }

    [TestMethod]
    public void ManifestPathFor_EmptyInput_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            SelectorManifestWriter.ManifestPathFor(string.Empty));
    }

    [TestMethod]
    public async Task WriteAsync_WritesValidJsonToDisk()
    {
        var manifest = new SelectorManifest(new[]
        {
            new SelectorEntry(
                Selector: "#submit",
                LocatorMethod: "Locator",
                SourceFile: "LoginTest.cs",
                SourceLine: 7,
                PageUrl: "https://example.com",
                Fingerprint: new DomFingerprint(
                    "button",
                    new Dictionary<string, string> { ["id"] = "submit" },
                    "Sign in",
                    "form",
                    "hash123"))
        });

        var tempDir = Path.Combine(Path.GetTempPath(), $"motus-manifest-test-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(tempDir, "LoginTest.selectors.json");

        try
        {
            await SelectorManifestWriter.WriteAsync(manifest, outputPath, CancellationToken.None);

            Assert.IsTrue(File.Exists(outputPath));
            var content = await File.ReadAllTextAsync(outputPath);

            var parsed = JsonSerializer.Deserialize(content, SelectorManifestJsonContext.Default.SelectorManifest);
            Assert.IsNotNull(parsed);
            Assert.AreEqual(1, parsed.Entries.Count);
            Assert.AreEqual("#submit", parsed.Entries[0].Selector);
            Assert.AreEqual(7, parsed.Entries[0].SourceLine);
            Assert.AreEqual("Sign in", parsed.Entries[0].Fingerprint.VisibleText);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }
}
