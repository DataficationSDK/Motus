using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Motus.Analyzers.Tests.Helpers;

internal static class CodeFixTestHelper
{
    public static async Task<string> ApplyCodeFixAsync<TAnalyzer, TCodeFix>(string source)
        where TAnalyzer : DiagnosticAnalyzer, new()
        where TCodeFix : CodeFixProvider, new()
    {
        var compilation = AnalyzerTestHelper.CreateCompilation(source);
        var analyzer = new TAnalyzer();
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer));
        var diagnostics = await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();

        if (diagnostics.IsEmpty)
            throw new InvalidOperationException("No diagnostics found to apply code fix to.");

        var diagnostic = diagnostics[0];

        var tree = compilation.SyntaxTrees.First();
        var workspace = new Microsoft.CodeAnalysis.AdhocWorkspace();
        var project = workspace.AddProject("TestProject", LanguageNames.CSharp);

        // Add references
        foreach (var reference in compilation.References)
            project = project.AddMetadataReference(reference);

        var document = project.AddDocument("Test.cs", await tree.GetTextAsync());

        var codeFix = new TCodeFix();
        CodeAction? codeAction = null;

        var context = new CodeFixContext(document, diagnostic, (action, _) =>
        {
            codeAction = action;
        }, CancellationToken.None);

        await codeFix.RegisterCodeFixesAsync(context);

        if (codeAction is null)
            throw new InvalidOperationException("No code fix registered.");

        var operations = await codeAction.GetOperationsAsync(CancellationToken.None);
        var applyOp = operations.OfType<ApplyChangesOperation>().First();
        var changedSolution = applyOp.ChangedSolution;
        var changedDocument = changedSolution.GetDocument(document.Id)!;
        var changedText = await changedDocument.GetTextAsync();

        return changedText.ToString();
    }
}
