namespace Motus.Recorder.CodeEmit;

/// <summary>
/// Generates framework-specific class and method boilerplate for MSTest, xUnit, and NUnit.
/// </summary>
internal static class FrameworkTemplate
{
    internal static string GetHeader(CodeEmitOptions options) => options.Framework.ToLowerInvariant() switch
    {
        "mstest" => GetMSTestHeader(options),
        "xunit" => GetXUnitHeader(options),
        "nunit" => GetNUnitHeader(options),
        _ => throw new ArgumentException($"Unsupported framework: {options.Framework}")
    };

    internal static string GetFooter(CodeEmitOptions options) => options.Framework.ToLowerInvariant() switch
    {
        "mstest" => GetMSTestFooter(),
        "xunit" => GetXUnitFooter(),
        "nunit" => GetNUnitFooter(),
        _ => throw new ArgumentException($"Unsupported framework: {options.Framework}")
    };

    private static string GetMSTestHeader(CodeEmitOptions o) =>
$$"""
using Motus.Abstractions;
using Motus.Testing.MSTest;

namespace {{o.Namespace}};

[TestClass]
public class {{o.TestClassName}} : MotusTestBase
{
    [TestMethod]
    public async Task {{o.TestMethodName}}()
    {
        var page = Page;

""";

    private static string GetMSTestFooter() =>
"""
    }
}
""";

    private static string GetXUnitHeader(CodeEmitOptions o) =>
$$"""
using Motus.Abstractions;
using Motus.Testing.xUnit;

namespace {{o.Namespace}};

public class {{o.TestClassName}} : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    public {{o.TestClassName}}(BrowserContextFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task {{o.TestMethodName}}()
    {
        var page = _fixture.Page;

""";

    private static string GetXUnitFooter() =>
"""
    }
}
""";

    private static string GetNUnitHeader(CodeEmitOptions o) =>
$$"""
using Motus.Abstractions;
using Motus.Testing.NUnit;

namespace {{o.Namespace}};

[TestFixture]
public class {{o.TestClassName}} : MotusTestBase
{
    [Test]
    public async Task {{o.TestMethodName}}()
    {
        var page = Page;

""";

    private static string GetNUnitFooter() =>
"""
    }
}
""";
}
