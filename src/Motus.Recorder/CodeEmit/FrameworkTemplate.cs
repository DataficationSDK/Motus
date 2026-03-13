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

[Collection(nameof(MotusCollection))]
public class {{o.TestClassName}} : IAsyncLifetime
{
    private readonly BrowserContextFixture _fixture;
    private IPage _page = null!;

    public {{o.TestClassName}}(BrowserContextFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _page = _fixture.Page;
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
    }

    [Fact]
    public async Task {{o.TestMethodName}}()
    {
        var page = _page;

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
