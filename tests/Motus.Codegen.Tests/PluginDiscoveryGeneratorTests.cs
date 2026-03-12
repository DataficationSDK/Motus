using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Motus.Codegen.Tests;

[TestClass]
public class PluginDiscoveryGeneratorTests
{
    private static readonly string AbstractionsAssemblyPath =
        typeof(Motus.Abstractions.IPlugin).Assembly.Location;

    private static GeneratorDriverRunResult RunGenerator(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.ModuleInitializerAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(AbstractionsAssemblyPath),
        };

        // Add runtime assemblies needed for compilation
        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")));

        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PluginDiscoveryGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        return driver.GetRunResult();
    }

    [TestMethod]
    public void NoPlugins_GeneratesEmptyRegistry()
    {
        var result = RunGenerator("namespace Test { class Foo { } }");

        Assert.AreEqual(1, result.GeneratedTrees.Length);
        var source = result.GeneratedTrees[0].ToString();
        StringAssert.Contains(source, "MotusPluginRegistry");
        StringAssert.Contains(source, "Array.Empty<IPlugin>()");
    }

    [TestMethod]
    public void SingleValidPlugin_GeneratesNewInstance()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public class MyPlugin : IPlugin
            {
                public string PluginId => "my-plugin";
                public string Name => "My Plugin";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        StringAssert.Contains(generated, "new global::Test.MyPlugin()");
    }

    [TestMethod]
    public void MultiplePlugins_AllAppearInArray()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public class PluginA : IPlugin
            {
                public string PluginId => "a";
                public string Name => "A";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }

            [MotusPlugin]
            public class PluginB : IPlugin
            {
                public string PluginId => "b";
                public string Name => "B";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        StringAssert.Contains(generated, "new global::Test.PluginA()");
        StringAssert.Contains(generated, "new global::Test.PluginB()");
    }

    [TestMethod]
    public void AbstractClass_EmitsMotus001Warning()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public abstract class AbstractPlugin : IPlugin
            {
                public string PluginId => "abs";
                public string Name => "Abstract";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        // Should not include the abstract class
        Assert.IsFalse(generated.Contains("AbstractPlugin"));

        // Should emit MOTUS001 warning
        var warnings = result.Diagnostics.Where(d => d.Id == "MOTUS001").ToList();
        Assert.AreEqual(1, warnings.Count);
    }

    [TestMethod]
    public void ClassWithoutIPlugin_EmitsMotus002Warning()
    {
        var source = """
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public class NotAPlugin { }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        Assert.IsFalse(generated.Contains("NotAPlugin"));

        var warnings = result.Diagnostics.Where(d => d.Id == "MOTUS002").ToList();
        Assert.AreEqual(1, warnings.Count);
    }

    [TestMethod]
    public void ClassWithoutParameterlessCtor_EmitsMotus003Warning()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public class NeedsArgPlugin : IPlugin
            {
                public NeedsArgPlugin(string config) { }
                public string PluginId => "needs-arg";
                public string Name => "NeedsArg";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        Assert.IsFalse(generated.Contains("NeedsArgPlugin"));

        var warnings = result.Diagnostics.Where(d => d.Id == "MOTUS003").ToList();
        Assert.AreEqual(1, warnings.Count);
    }

    [TestMethod]
    public void GenericClass_EmitsMotus004Warning()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public class GenericPlugin<T> : IPlugin
            {
                public string PluginId => "generic";
                public string Name => "Generic";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }
            """;

        var result = RunGenerator(source);
        var generated = result.GeneratedTrees[0].ToString();

        Assert.IsFalse(generated.Contains("GenericPlugin"));

        var warnings = result.Diagnostics.Where(d => d.Id == "MOTUS004").ToList();
        Assert.AreEqual(1, warnings.Count);
    }

    [TestMethod]
    public void GeneratedSource_CompilesWithoutErrors()
    {
        var source = """
            using System.Threading.Tasks;
            using Motus.Abstractions;

            namespace Test;

            [MotusPlugin]
            public class CompilablePlugin : IPlugin
            {
                public string PluginId => "compilable";
                public string Name => "Compilable";
                public string Version => "1.0.0";
                public string? Author => null;
                public string? Description => null;
                public Task OnLoadedAsync(IPluginContext context) => Task.CompletedTask;
                public Task OnUnloadedAsync() => Task.CompletedTask;
            }
            """;

        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.ModuleInitializerAttribute).Assembly.Location),
            MetadataReference.CreateFromFile(AbstractionsAssemblyPath),
        };

        var runtimeDir = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(System.IO.Path.Combine(runtimeDir, "System.Runtime.dll")));

        var compilation = CSharpCompilation.Create("TestAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new PluginDiscoveryGenerator();
        var driver = CSharpGeneratorDriver.Create(generator);

        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);

        var errors = outputCompilation.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();

        Assert.AreEqual(0, errors.Count,
            $"Generated source has compilation errors: {string.Join("; ", errors.Select(d => d.GetMessage()))}");
    }
}
