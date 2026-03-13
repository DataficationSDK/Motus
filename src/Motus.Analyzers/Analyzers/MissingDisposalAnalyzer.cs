using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MissingDisposalAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.MissingDisposal,
        "Browser or context not disposed with await using",
        "'{0}' implements IAsyncDisposable and should be declared with 'await using'",
        AnalyzerCategories.Automation,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var localDecl = (LocalDeclarationStatementSyntax)context.Node;

        // Check each variable declarator
        foreach (var variable in localDecl.Declaration.Variables)
        {
            if (variable.Initializer is null) continue;

            var typeInfo = context.SemanticModel.GetTypeInfo(variable.Initializer.Value, context.CancellationToken);
            var type = typeInfo.Type;

            if (type is null) continue;

            // Check if the resolved type is or implements IBrowser or IBrowserContext
            if (!IsMotusDisposable(type))
                continue;

            // Check for await and using keywords
            bool hasAwait = localDecl.AwaitKeyword != default;
            bool hasUsing = localDecl.UsingKeyword != default;

            if (hasAwait && hasUsing) continue;

            context.ReportDiagnostic(Diagnostic.Create(Rule, localDecl.GetLocation(), variable.Identifier.Text));
        }
    }

    private static bool IsMotusDisposable(ITypeSymbol type)
    {
        var fqn = SymbolHelper.GetFullyQualifiedName(type);
        if (fqn == KnownTypeNames.IBrowser || fqn == KnownTypeNames.IBrowserContext)
            return true;
        return SymbolHelper.ImplementsAny(type, KnownTypeNames.IBrowser, KnownTypeNames.IBrowserContext);
    }
}
