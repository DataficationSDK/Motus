using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnusedLocatorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.UnusedLocator,
        "Locator result is not used",
        "Result of '{0}' is discarded; locators are lazy and have no effect unless acted upon",
        AnalyzerCategories.Automation,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly string[] LocatorCreatingMethods =
    {
        "Locator", "GetByRole", "GetByText", "GetByLabel",
        "GetByPlaceholder", "GetByTestId", "GetByTitle", "GetByAltText",
        "First", "Last", "Nth", "Filter"
    };

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

        // Must be a standalone expression statement (result discarded)
        if (invocation.Parent is not ExpressionStatementSyntax)
            return;

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        bool isLocatorMethod = false;
        foreach (var name in LocatorCreatingMethods)
        {
            if (method.Name == name)
            {
                isLocatorMethod = true;
                break;
            }
        }
        if (!isLocatorMethod) return;

        // Must be on a Motus interface type
        if (!SymbolHelper.IsContainingTypeOneOf(method,
                KnownTypeNames.IPage, KnownTypeNames.ILocator, KnownTypeNames.IFrame)
            && !SymbolHelper.ImplementsAny(method.ContainingType,
                KnownTypeNames.IPage, KnownTypeNames.ILocator, KnownTypeNames.IFrame))
            return;

        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), method.Name));
    }
}
