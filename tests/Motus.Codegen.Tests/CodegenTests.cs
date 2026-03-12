using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Motus.Codegen.Tests;

[TestClass]
public class CodegenTests
{
    private static string LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "minimal_protocol.json");
        return File.ReadAllText(path);
    }

    private static GeneratorDriverRunResult RunGenerator(params (string fileName, string content)[] additionalTexts)
    {
        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText("")],
            references:
            [
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.JsonElement).Assembly.Location),
                MetadataReference.CreateFromFile(typeof(System.Text.Json.Serialization.JsonConverterAttribute).Assembly.Location),
            ],
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new CdpGenerator();
        var driver = CSharpGeneratorDriver.Create(generator)
            .AddAdditionalTexts(additionalTexts
                .Select(t => (AdditionalText)new InMemoryAdditionalText(t.fileName, t.content))
                .ToImmutableArray());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out _, out _);
        return driver.GetRunResult();
    }

    [TestMethod]
    public void Generator_EmitsSourceForEachDomain()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        Assert.AreEqual(2, result.GeneratedTrees.Length,
            "Expected one generated file per domain");
    }

    [TestMethod]
    public void Generator_GeneratedSourceHasNoSyntaxErrors()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        foreach (var tree in result.GeneratedTrees)
        {
            var syntaxDiags = tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
            Assert.AreEqual(0, syntaxDiags.Count,
                $"Syntax errors in {tree.FilePath}: {string.Join("; ", syntaxDiags.Select(d => d.GetMessage()))}");
        }
    }

    [TestMethod]
    public void Generator_ContainsExpectedTypes()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        var allSource = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        // Domain class
        StringAssert.Contains(allSource, "public static partial class TestDomainDomain");
        StringAssert.Contains(allSource, "public static partial class CrossRefDomain");

        // Types
        StringAssert.Contains(allSource, "public sealed record FrameInfo(");
        StringAssert.Contains(allSource, "public enum ResourceType");

        // Commands
        StringAssert.Contains(allSource, "public sealed record NavigateParams(");
        StringAssert.Contains(allSource, "public sealed record NavigateResponse(");
        StringAssert.Contains(allSource, "public sealed record DisableParams()");
        StringAssert.Contains(allSource, "public sealed record DisableResponse()");

        // Events
        StringAssert.Contains(allSource, "public sealed record FrameNavigatedEvent(");
        StringAssert.Contains(allSource, "public sealed record LoadEventFiredEvent(");
    }

    [TestMethod]
    public void Generator_OptionalParametersHaveDefaultValues()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        var allSource = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        StringAssert.Contains(allSource, "string? Referrer = default");
    }

    [TestMethod]
    public void Generator_CrossDomainRefsResolveCorrectly()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        var allSource = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        StringAssert.Contains(allSource, "Motus.Protocol.TestDomainDomain.FrameInfo Info");
    }

    [TestMethod]
    public void Generator_NoDiagnostics()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.AreEqual(0, errors.Count,
            $"Generator diagnostics: {string.Join("; ", errors.Select(d => d.GetMessage()))}");
    }

    [TestMethod]
    public void Generator_WithNoAdditionalFiles_EmitsNothing()
    {
        var result = RunGenerator();
        Assert.AreEqual(0, result.GeneratedTrees.Length);
    }

    [TestMethod]
    public void Generator_EnumHasJsonStringEnumMemberName()
    {
        var fixture = LoadFixture();
        var result = RunGenerator(("browser_protocol.json", fixture));

        var allSource = string.Join("\n", result.GeneratedTrees.Select(t => t.ToString()));

        StringAssert.Contains(allSource, "[JsonStringEnumMemberName(\"document\")]");
        StringAssert.Contains(allSource, "[JsonStringEnumMemberName(\"stylesheet\")]");
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText _text;

        public InMemoryAdditionalText(string path, string content)
        {
            Path = path;
            _text = SourceText.From(content);
        }

        public override string Path { get; }
        public override SourceText? GetText(CancellationToken cancellationToken = default) => _text;
    }
}
