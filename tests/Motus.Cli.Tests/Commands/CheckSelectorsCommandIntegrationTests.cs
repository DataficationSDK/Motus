using System.Text.Json;
using Motus.Abstractions;
using Motus.Cli.Services;
using Motus.Selectors;

namespace Motus.Cli.Tests.Commands;

[TestClass]
[TestCategory("Integration")]
public class CheckSelectorsCommandIntegrationTests
{
    private static readonly string CwdOriginal = Directory.GetCurrentDirectory();
    private static bool s_browserAvailable;

    private string _workDir = null!;

    [ClassInitialize]
    public static async Task ClassSetup(TestContext _)
    {
        try
        {
            var probe = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
            await probe.CloseAsync();
            s_browserAvailable = true;
        }
        catch (FileNotFoundException)
        {
            s_browserAvailable = false;
        }
    }

    [TestInitialize]
    public void Setup()
    {
        if (!s_browserAvailable)
            Assert.Inconclusive("No browser found; skipping integration tests.");

        _workDir = Path.Combine(Path.GetTempPath(), $"motus-check-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workDir);
        Directory.SetCurrentDirectory(_workDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.SetCurrentDirectory(CwdOriginal);
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    private void WriteSource(string filename, string body)
    {
        var src = $$"""
            class T {
                void M(dynamic page) {
            {{body}}
                }
            }
            """;
        File.WriteAllText(Path.Combine(_workDir, filename), src);
    }

    [TestMethod]
    public async Task Run_HealthySelector_ReportsHealthy()
    {
        WriteSource("Healthy.cs", "        page.GetByTestId(\"submit\");");
        var url = "data:text/html,<button data-testid='submit'>Submit</button>";

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var runner = new CheckSelectorsRunner(stdout, stderr, useColor: false);

        var code = await runner.RunAsync("*.cs", manifestPath: null, baseUrl: url, ci: false, jsonOutputPath: null, CancellationToken.None);

        Assert.AreEqual(0, code);
        StringAssert.Contains(stdout.ToString(), "HEALTHY");
        StringAssert.Contains(stdout.ToString(), "1 healthy");
    }

    [TestMethod]
    public async Task Run_BrokenSelector_ReportsBroken_NoCiExitZero()
    {
        WriteSource("Broken.cs", "        page.Locator(\"#does-not-exist\");");
        var url = "data:text/html,<p>nothing here</p>";

        var stdout = new StringWriter();
        var runner = new CheckSelectorsRunner(stdout, new StringWriter(), useColor: false);

        var code = await runner.RunAsync("*.cs", null, url, ci: false, null, CancellationToken.None);

        Assert.AreEqual(0, code, "Without --ci, broken selectors should not fail the run");
        StringAssert.Contains(stdout.ToString(), "BROKEN");
    }

    [TestMethod]
    public async Task Run_BrokenSelector_WithCi_ReturnsOne()
    {
        WriteSource("BrokenCi.cs", "        page.Locator(\"#nope\");");
        var url = "data:text/html,<p>nothing</p>";

        var runner = new CheckSelectorsRunner(new StringWriter(), new StringWriter(), useColor: false);

        var code = await runner.RunAsync("*.cs", null, url, ci: true, null, CancellationToken.None);

        Assert.AreEqual(1, code);
    }

    [TestMethod]
    public async Task Run_AmbiguousSelector_ReportsAmbiguous()
    {
        WriteSource("Ambiguous.cs", "        page.Locator(\"button\");");
        var url = "data:text/html,<button>A</button><button>B</button>";

        var stdout = new StringWriter();
        var runner = new CheckSelectorsRunner(stdout, new StringWriter(), useColor: false);

        var code = await runner.RunAsync("*.cs", null, url, ci: false, null, CancellationToken.None);

        Assert.AreEqual(0, code);
        StringAssert.Contains(stdout.ToString(), "AMBIGUOUS");
    }

    [TestMethod]
    public async Task Run_InterpolatedSelector_IsSkipped()
    {
        WriteSource("Interp.cs", "        var id = \"x\"; page.Locator($\"#{id}\");");
        var url = "data:text/html,<p>hi</p>";

        var stdout = new StringWriter();
        var runner = new CheckSelectorsRunner(stdout, new StringWriter(), useColor: false);

        var code = await runner.RunAsync("*.cs", null, url, ci: true, null, CancellationToken.None);

        Assert.AreEqual(0, code, "Interpolated selectors must not count as broken under --ci");
        StringAssert.Contains(stdout.ToString(), "SKIPPED");
    }

    [TestMethod]
    public async Task Run_JsonOutput_WritesDeserializableFile()
    {
        WriteSource("Json.cs", "        page.GetByTestId(\"submit\");");
        var url = "data:text/html,<button data-testid='submit'>Go</button>";
        var jsonPath = Path.Combine(_workDir, "results.json");

        var runner = new CheckSelectorsRunner(new StringWriter(), new StringWriter(), useColor: false);
        var code = await runner.RunAsync("*.cs", null, url, ci: false, jsonPath, CancellationToken.None);

        Assert.AreEqual(0, code);
        Assert.IsTrue(File.Exists(jsonPath));

        var content = await File.ReadAllTextAsync(jsonPath);
        var parsed = JsonSerializer.Deserialize(content, CheckResultsJsonContext.Default.ListSelectorCheckResult);

        Assert.IsNotNull(parsed);
        Assert.AreEqual(1, parsed!.Count);
        Assert.AreEqual(SelectorCheckStatus.Healthy, parsed[0].Status);
        Assert.AreEqual(1, parsed[0].MatchCount);
    }

    [TestMethod]
    public async Task Run_ManifestWithFingerprint_ProducesSuggestionForBrokenSelector()
    {
        // Source: test code references the old selector id.
        WriteSource("Fp.cs", "        page.GetByTestId(\"old-id\");");

        // HTML: button exists but with a different data-testid, matching the
        // fingerprint's key attributes.
        var url = "data:text/html,<button data-testid='new-id'>Go</button>";

        // Build a manifest entry whose fingerprint describes the live button.
        // The hash is computed canonically so the strict path catches it;
        // the attribute-match fallback is also sufficient if outerHTML differs.
        var fingerprint = new DomFingerprint(
            TagName: "button",
            KeyAttributes: new Dictionary<string, string> { ["data-testid"] = "new-id" },
            VisibleText: "Go",
            AncestorPath: "html > body",
            Hash: DomFingerprintBuilder.ComputeHash(
                "button",
                new Dictionary<string, string> { ["data-testid"] = "new-id" },
                "Go",
                "html > body"));

        var manifest = new SelectorManifest(new[]
        {
            new SelectorEntry(
                Selector: "old-id",
                LocatorMethod: "GetByTestId",
                SourceFile: Path.Combine(_workDir, "Fp.cs"),
                SourceLine: 3,
                PageUrl: url,
                Fingerprint: fingerprint),
        });

        var manifestPath = Path.Combine(_workDir, "Fp.selectors.json");
        await SelectorManifestWriter.WriteAsync(manifest, manifestPath);

        var stdout = new StringWriter();
        var runner = new CheckSelectorsRunner(stdout, new StringWriter(), useColor: false);

        var code = await runner.RunAsync("*.cs", manifestPath, baseUrl: null, ci: false, null, CancellationToken.None);

        Assert.AreEqual(0, code);
        var output = stdout.ToString();
        StringAssert.Contains(output, "BROKEN");
        StringAssert.Contains(output, "Suggestion: GetByTestId(\"new-id\")");
    }
}
