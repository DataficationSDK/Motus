using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DeprecatedSelectorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.DeprecatedSelector,
        "Selector uses deprecated engine prefix",
        "Selector prefix '{0}' is deprecated; use the dedicated locator method or omit the prefix",
        AnalyzerCategories.Usage,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly string[] DeprecatedPrefixes = { "css=", "xpath=", "text=", "id=" };

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

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        if (method.Name != "Locator") return;

        if (!SymbolHelper.IsContainingTypeOneOf(method, KnownTypeNames.IPage, KnownTypeNames.ILocator, KnownTypeNames.IFrame)
            && !SymbolHelper.ImplementsAny(method.ContainingType, KnownTypeNames.IPage, KnownTypeNames.ILocator, KnownTypeNames.IFrame))
            return;

        var args = invocation.ArgumentList.Arguments;
        if (args.Count == 0) return;

        if (args[0].Expression is not LiteralExpressionSyntax literal
            || !literal.IsKind(SyntaxKind.StringLiteralExpression))
            return;

        var selector = literal.Token.ValueText;

        foreach (var prefix in DeprecatedPrefixes)
        {
            if (selector.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), prefix));
                return;
            }
        }
    }
}
