using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class HardcodedDelayAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.HardcodedDelay,
        "Avoid hardcoded delays in browser tests",
        "Replace '{0}' with an explicit wait condition such as WaitForLoadStateAsync",
        AnalyzerCategories.Usage,
        DiagnosticSeverity.Info,
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

        var symbolInfo = context.SemanticModel.GetSymbolInfo(invocation, context.CancellationToken);
        if (symbolInfo.Symbol is not IMethodSymbol method)
            return;

        var containingTypeFqn = method.ContainingType is not null
            ? SymbolHelper.GetFullyQualifiedName(method.ContainingType)
            : null;

        if (containingTypeFqn is null) return;

        bool isDelay = (containingTypeFqn == KnownTypeNames.TaskType && method.Name == "Delay")
                    || (containingTypeFqn == KnownTypeNames.ThreadType && method.Name == "Sleep");

        if (!isDelay) return;

        var callText = containingTypeFqn == KnownTypeNames.TaskType ? "Task.Delay" : "Thread.Sleep";
        context.ReportDiagnostic(Diagnostic.Create(Rule, invocation.GetLocation(), callText));
    }
}
