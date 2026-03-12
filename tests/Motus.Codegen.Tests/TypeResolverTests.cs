using System.Collections.Immutable;
using Motus.Codegen.Emit;
using Motus.Codegen.Model;

namespace Motus.Codegen.Tests;

[TestClass]
public class TypeResolverTests
{
    private static TypeResolver CreateResolver()
    {
        var domains = ImmutableArray.Create(
            new CdpDomain("Page",
                ImmutableArray.Create(
                    new CdpType("FrameId", "string", CdpTypeKind.Alias,
                        ImmutableArray<string>.Empty, ImmutableArray<CdpProperty>.Empty,
                        null, null, false, false),
                    new CdpType("Frame", "object", CdpTypeKind.Object,
                        ImmutableArray<string>.Empty,
                        ImmutableArray.Create(
                            new CdpProperty("id", null, "string", false, null, null, false, ImmutableArray<string>.Empty),
                            new CdpProperty("url", null, "string", false, null, null, false, ImmutableArray<string>.Empty)),
                        null, null, false, false),
                    new CdpType("TransitionType", "string", CdpTypeKind.StringEnum,
                        ImmutableArray.Create("link", "typed"),
                        ImmutableArray<CdpProperty>.Empty,
                        null, null, false, false),
                    new CdpType("NodeIds", "array", CdpTypeKind.ArrayType,
                        ImmutableArray<string>.Empty, ImmutableArray<CdpProperty>.Empty,
                        null, "integer", false, false)
                ),
                ImmutableArray<CdpCommand>.Empty,
                ImmutableArray<CdpEvent>.Empty,
                false, false),
            new CdpDomain("Network",
                ImmutableArray.Create(
                    new CdpType("LoaderId", "string", CdpTypeKind.Alias,
                        ImmutableArray<string>.Empty, ImmutableArray<CdpProperty>.Empty,
                        null, null, false, false)
                ),
                ImmutableArray<CdpCommand>.Empty,
                ImmutableArray<CdpEvent>.Empty,
                false, false)
        );

        return new TypeResolver(domains);
    }

    [TestMethod]
    public void ResolveRef_IntraDomain_ResolvesStringAlias()
    {
        var resolver = CreateResolver();
        Assert.AreEqual("string", resolver.ResolveRef("FrameId", "Page"));
    }

    [TestMethod]
    public void ResolveRef_IntraDomain_ResolvesObjectType()
    {
        var resolver = CreateResolver();
        Assert.AreEqual("Motus.Protocol.PageDomain.Frame", resolver.ResolveRef("Frame", "Page"));
    }

    [TestMethod]
    public void ResolveRef_IntraDomain_ResolvesEnumType()
    {
        var resolver = CreateResolver();
        Assert.AreEqual("Motus.Protocol.PageDomain.TransitionType", resolver.ResolveRef("TransitionType", "Page"));
    }

    [TestMethod]
    public void ResolveRef_CrossDomain_ResolvesCorrectly()
    {
        var resolver = CreateResolver();
        Assert.AreEqual("string", resolver.ResolveRef("Network.LoaderId", "Page"));
    }

    [TestMethod]
    public void ResolveRef_UnknownRef_FallsBackToJsonElement()
    {
        var resolver = CreateResolver();
        Assert.AreEqual("System.Text.Json.JsonElement", resolver.ResolveRef("Unknown.Type", "Page"));
    }

    [TestMethod]
    public void Resolve_OptionalProperty_ReturnsNullable()
    {
        var resolver = CreateResolver();
        var prop = new CdpProperty("test", null, "string", true, null, null, false, ImmutableArray<string>.Empty);
        Assert.AreEqual("string?", resolver.Resolve(prop, "Page"));
    }

    [TestMethod]
    public void Resolve_RequiredProperty_ReturnsNonNullable()
    {
        var resolver = CreateResolver();
        var prop = new CdpProperty("test", null, "string", false, null, null, false, ImmutableArray<string>.Empty);
        Assert.AreEqual("string", resolver.Resolve(prop, "Page"));
    }

    [TestMethod]
    public void Resolve_PrimitiveTypes_MapCorrectly()
    {
        var resolver = CreateResolver();

        var intProp = new CdpProperty("n", null, "integer", false, null, null, false, ImmutableArray<string>.Empty);
        Assert.AreEqual("long", resolver.Resolve(intProp, "Page"));

        var numProp = new CdpProperty("n", null, "number", false, null, null, false, ImmutableArray<string>.Empty);
        Assert.AreEqual("double", resolver.Resolve(numProp, "Page"));

        var boolProp = new CdpProperty("b", null, "boolean", false, null, null, false, ImmutableArray<string>.Empty);
        Assert.AreEqual("bool", resolver.Resolve(boolProp, "Page"));
    }

    [TestMethod]
    public void Resolve_ArrayProperty_ResolvesItemType()
    {
        var resolver = CreateResolver();
        var prop = new CdpProperty("ids", null, "array", false, null, "string", false, ImmutableArray<string>.Empty);
        Assert.AreEqual("string[]", resolver.Resolve(prop, "Page"));
    }

    [TestMethod]
    public void Resolve_ArrayPropertyWithRef_ResolvesItemRef()
    {
        var resolver = CreateResolver();
        var prop = new CdpProperty("frames", null, "array", false, "Frame", null, false, ImmutableArray<string>.Empty);
        Assert.AreEqual("Motus.Protocol.PageDomain.Frame[]", resolver.Resolve(prop, "Page"));
    }

    [TestMethod]
    public void ResolveRef_ArrayAlias_ResolvesCorrectly()
    {
        var resolver = CreateResolver();
        Assert.AreEqual("long[]", resolver.ResolveRef("NodeIds", "Page"));
    }
}
