using System.Text.Json;
using Motus.Selectors;

namespace Motus.Tests.Selectors;

[TestClass]
public class SelectorManifestSerializationTests
{
    [TestMethod]
    public void RoundTrip_PreservesAllFields()
    {
        var fingerprint = new DomFingerprint(
            TagName: "button",
            KeyAttributes: new Dictionary<string, string> { ["id"] = "submit", ["role"] = "button" },
            VisibleText: "Sign in",
            AncestorPath: "div > form > section",
            Hash: "abc123");

        var entry = new SelectorEntry(
            Selector: "#submit",
            LocatorMethod: "Locator",
            SourceFile: "/tmp/LoginTest.cs",
            SourceLine: 42,
            PageUrl: "https://example.com/login",
            Fingerprint: fingerprint);

        var manifest = new SelectorManifest(new[] { entry });

        var json = JsonSerializer.Serialize(manifest, SelectorManifestJsonContext.Default.SelectorManifest);
        var deserialized = JsonSerializer.Deserialize(json, SelectorManifestJsonContext.Default.SelectorManifest);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(1, deserialized.Entries.Count);

        var round = deserialized.Entries[0];
        Assert.AreEqual("#submit", round.Selector);
        Assert.AreEqual("Locator", round.LocatorMethod);
        Assert.AreEqual("/tmp/LoginTest.cs", round.SourceFile);
        Assert.AreEqual(42, round.SourceLine);
        Assert.AreEqual("https://example.com/login", round.PageUrl);
        Assert.AreEqual("button", round.Fingerprint.TagName);
        Assert.AreEqual("Sign in", round.Fingerprint.VisibleText);
        Assert.AreEqual("div > form > section", round.Fingerprint.AncestorPath);
        Assert.AreEqual("abc123", round.Fingerprint.Hash);
        Assert.AreEqual("submit", round.Fingerprint.KeyAttributes["id"]);
        Assert.AreEqual("button", round.Fingerprint.KeyAttributes["role"]);
    }

    [TestMethod]
    public void Serialize_UsesCamelCase()
    {
        var manifest = new SelectorManifest(new[]
        {
            new SelectorEntry(
                Selector: "#x",
                LocatorMethod: "Locator",
                SourceFile: "a.cs",
                SourceLine: 1,
                PageUrl: "https://x",
                Fingerprint: new DomFingerprint("div", new Dictionary<string, string>(), null, "", "h"))
        });

        var json = JsonSerializer.Serialize(manifest, SelectorManifestJsonContext.Default.SelectorManifest);

        StringAssert.Contains(json, "\"entries\"");
        StringAssert.Contains(json, "\"selector\"");
        StringAssert.Contains(json, "\"locatorMethod\"");
        StringAssert.Contains(json, "\"sourceFile\"");
        StringAssert.Contains(json, "\"sourceLine\"");
        StringAssert.Contains(json, "\"pageUrl\"");
        StringAssert.Contains(json, "\"fingerprint\"");
        StringAssert.Contains(json, "\"tagName\"");
        StringAssert.Contains(json, "\"keyAttributes\"");
        StringAssert.Contains(json, "\"ancestorPath\"");
    }

    [TestMethod]
    public void Serialize_OmitsNullVisibleText()
    {
        var manifest = new SelectorManifest(new[]
        {
            new SelectorEntry(
                Selector: "#x",
                LocatorMethod: "Locator",
                SourceFile: "a.cs",
                SourceLine: 1,
                PageUrl: "https://x",
                Fingerprint: new DomFingerprint("div", new Dictionary<string, string>(), VisibleText: null, "", "h"))
        });

        var json = JsonSerializer.Serialize(manifest, SelectorManifestJsonContext.Default.SelectorManifest);

        Assert.IsFalse(json.Contains("\"visibleText\""), $"Expected visibleText omitted; JSON: {json}");
    }
}
