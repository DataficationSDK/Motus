using System.Text;
using System.Text.Json;

namespace Motus.Tests.Coverage;

[TestClass]
public class SourceMapResolverTests
{
    [TestMethod]
    public void ExtractMapReference_JsComment_Found()
    {
        var src = "console.log(1);\n//# sourceMappingURL=bundle.js.map\n";
        var url = SourceMapResolver.ExtractMapReference(src);
        Assert.AreEqual("bundle.js.map", url);
    }

    [TestMethod]
    public void ExtractMapReference_CssComment_Found()
    {
        var src = ".x{color:red}\n/*# sourceMappingURL=app.css.map */\n";
        var url = SourceMapResolver.ExtractMapReference(src);
        Assert.AreEqual("app.css.map", url);
    }

    [TestMethod]
    public void ExtractMapReference_OutsideTailWindow_NotFound()
    {
        // Place the comment at the start, then pad with > 2KB of non-comment content.
        var prefix = "//# sourceMappingURL=early.map\n";
        var pad = new string('x', 4096);
        var src = prefix + pad;
        var url = SourceMapResolver.ExtractMapReference(src);
        Assert.IsNull(url);
    }

    [TestMethod]
    public void ExtractMapReference_NoComment_ReturnsNull()
    {
        Assert.IsNull(SourceMapResolver.ExtractMapReference("console.log(1);"));
    }

    [TestMethod]
    public async Task TryResolveAsync_InlineDataUri_Decodes()
    {
        var json = JsonSerializer.Serialize(new
        {
            version = 3,
            sources = new[] { "a.ts" },
            sourcesContent = new[] { "let x = 1;\n" },
            mappings = "AAAA"
        });
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var src = $"console.log(1);\n//# sourceMappingURL=data:application/json;base64,{b64}\n";

        var resolver = new SourceMapResolver(new SourceMapFetcher());
        var map = await resolver.TryResolveAsync(src, "https://example.com/bundle.js", CancellationToken.None);

        Assert.IsNotNull(map);
        Assert.AreEqual(3, map!.Version);
        Assert.AreEqual("a.ts", map.Sources[0]);
        Assert.AreEqual("let x = 1;\n", map.SourcesContent[0]);
    }

    [TestMethod]
    public async Task TryResolveAsync_InlineDataUri_PercentEncoded_Decodes()
    {
        // Some bundlers emit non-base64 data URIs.
        var json = "{\"version\":3,\"sources\":[\"a.ts\"],\"mappings\":\"AAAA\"}";
        var encoded = Uri.EscapeDataString(json);
        var src = $"a;\n//# sourceMappingURL=data:application/json,{encoded}\n";

        var resolver = new SourceMapResolver(new SourceMapFetcher());
        var map = await resolver.TryResolveAsync(src, "https://example.com/x.js", CancellationToken.None);

        Assert.IsNotNull(map);
        Assert.AreEqual("a.ts", map!.Sources[0]);
    }

    [TestMethod]
    public async Task TryResolveAsync_MalformedJson_ReturnsNull()
    {
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("not json"));
        var src = $"x;\n//# sourceMappingURL=data:application/json;base64,{b64}\n";

        var resolver = new SourceMapResolver(new SourceMapFetcher());
        var map = await resolver.TryResolveAsync(src, "https://example.com/x.js", CancellationToken.None);

        Assert.IsNull(map);
    }

    [TestMethod]
    public async Task TryResolveAsync_WrongVersion_ReturnsNull()
    {
        var json = JsonSerializer.Serialize(new { version = 2, sources = new[] { "a.ts" }, mappings = "" });
        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var src = $"x;\n//# sourceMappingURL=data:application/json;base64,{b64}\n";

        var resolver = new SourceMapResolver(new SourceMapFetcher());
        var map = await resolver.TryResolveAsync(src, "https://example.com/x.js", CancellationToken.None);

        Assert.IsNull(map);
    }

    [TestMethod]
    public async Task TryResolveAsync_NoSourceMappingURL_ReturnsNull()
    {
        var resolver = new SourceMapResolver(new SourceMapFetcher());
        var map = await resolver.TryResolveAsync("console.log(1);", "https://example.com/x.js", CancellationToken.None);
        Assert.IsNull(map);
    }
}
