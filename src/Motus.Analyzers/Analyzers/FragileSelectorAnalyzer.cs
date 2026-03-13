using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FragileSelectorAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.FragileSelector,
        "Selector appears fragile",
        "Selector '{0}' appears fragile: {1}",
        AnalyzerCategories.Usage,
        DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    private static readonly Regex DeepNesting = new(@"([\s>]+[a-zA-Z.*#\[]+){4,}", RegexOptions.Compiled);
    private static readonly Regex NthChildChain = new(@":nth-child\(.*?\).*:nth-child\(", RegexOptions.Compiled);
    private static readonly Regex AutoGenClass = new(@"[a-zA-Z]+-[a-f0-9]{5,8}\b", RegexOptions.Compiled);

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

        if (DeepNesting.IsMatch(selector))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), selector, "deeply nested selector"));
            return;
        }

        if (NthChildChain.IsMatch(selector))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), selector, "chained :nth-child selectors"));
            return;
        }

        if (AutoGenClass.IsMatch(selector))
        {
            context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), selector, "auto-generated class name"));
        }
    }
}
