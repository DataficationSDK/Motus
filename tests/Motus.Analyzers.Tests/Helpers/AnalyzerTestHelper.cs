using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Motus.Analyzers.Tests.Helpers;

internal static class AnalyzerTestHelper
{
    private static readonly string AbstractionsAssemblyPath =
        typeof(Motus.Abstractions.IPage).Assembly.Location;

    public static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync<TAnalyzer>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
    {
        var compilation = CreateCompilation(source);
        var analyzer = new TAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    public static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(source));

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(AbstractionsAssemblyPath),
        };

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        references.Add(MetadataReference.CreateFromFile(Path.Combine(runtimeDir, "System.Runtime.dll")));

        // Add System.Threading.Tasks for Task/Task<T>
        var taskAssembly = typeof(System.Threading.Tasks.Task).Assembly.Location;
        if (!references.Any(r => r.Display == taskAssembly))
            references.Add(MetadataReference.CreateFromFile(taskAssembly));

        // Add System.Threading for Thread
        var threadAssembly = typeof(System.Threading.Thread).Assembly.Location;
        if (!references.Any(r => r.Display == threadAssembly))
            references.Add(MetadataReference.CreateFromFile(threadAssembly));

        // Add netstandard
        var netstdPath = Path.Combine(runtimeDir, "netstandard.dll");
        if (File.Exists(netstdPath))
            references.Add(MetadataReference.CreateFromFile(netstdPath));

        // Add System.Console (sometimes needed)
        var consolePath = Path.Combine(runtimeDir, "System.Console.dll");
        if (File.Exists(consolePath))
            references.Add(MetadataReference.CreateFromFile(consolePath));

        return CSharpCompilation.Create("TestAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
