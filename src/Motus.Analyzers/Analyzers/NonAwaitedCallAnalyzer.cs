using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NonAwaitedCallAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.NonAwaitedCall,
        "Async Motus call is not awaited",
        "Call to '{0}' on Motus type is not awaited",
        AnalyzerCategories.Automation,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeInvocation, SyntaxKind.InvocationExpression);
    }

    private static void AnalyzeInvocation(SyntaxNodeAnalysisContext context)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;

        // Must be a standalone expression statement (not awaited, not assigned, etc.)
        if (invocation.Parent is not ExpressionStatementSyntax)
            return;

        // Must not already be awaited
        if (invocation.Parent.Parent is AwaitExpressionSyntax)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        // Must return Task/Task<T>
        if (!SymbolHelper.IsTaskType(method.ReturnType))
            return;

        // Containing type must implement a Motus interface
        if (!SymbolHelper.ImplementsAny(method.ContainingType,
                KnownTypeNames.IPage, KnownTypeNames.ILocator,
                KnownTypeNames.IBrowser, KnownTypeNames.IBrowserContext,
                KnownTypeNames.IFrame))
        {
            // Also check if the method is defined directly on the interface
            if (!SymbolHelper.IsContainingTypeOneOf(method,
                    KnownTypeNames.IPage, KnownTypeNames.ILocator,
                    KnownTypeNames.IBrowser, KnownTypeNames.IBrowserContext,
                    KnownTypeNames.IFrame))
                return;
        }

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.Name));
    }
}
