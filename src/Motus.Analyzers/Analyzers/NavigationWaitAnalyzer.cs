using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Motus.Analyzers.Analyzers;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class NavigationWaitAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        DiagnosticIds.NavigationWait,
        "Navigation not followed by a wait",
        "Navigation call '{0}' is not followed by a wait; consider adding WaitForLoadStateAsync or WaitForURLAsync",
        AnalyzerCategories.Automation,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly string[] NavigationMethods =
    {
        "GotoAsync", "GoBackAsync", "GoForwardAsync", "ReloadAsync"
    };

    private static readonly string[] WaitMethods =
    {
        "WaitForLoadStateAsync", "WaitForURLAsync", "WaitForRequestAsync",
        "WaitForResponseAsync", "WaitForFunctionAsync", "WaitForTimeoutAsync"
    };

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCodeBlockAction(AnalyzeCodeBlock);
    }

    private static void AnalyzeCodeBlock(CodeBlockAnalysisContext context)
    {
        var block = context.CodeBlock;

        // Find all blocks within the code block
        foreach (var descendant in block.DescendantNodes())
        {
            if (descendant is not BlockSyntax blockSyntax) continue;

            var statements = blockSyntax.Statements;
            for (int i = 0; i < statements.Count; i++)
            {
                var navMethodName = GetNavigationMethodName(statements[i], context.SemanticModel);
                if (navMethodName is null) continue;

                // Check if the next statement is a wait
                if (i + 1 < statements.Count && IsWaitStatement(statements[i + 1], context.SemanticModel))
                    continue;

                var location = statements[i].GetLocation();
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, navMethodName));
            }
        }
    }

    private static string? GetNavigationMethodName(StatementSyntax statement, SemanticModel model)
    {
        InvocationExpressionSyntax? invocation = null;

        if (statement is ExpressionStatementSyntax exprStmt)
        {
            // Handle: await page.GotoAsync(...)
            if (exprStmt.Expression is AwaitExpressionSyntax awaitExpr
                && awaitExpr.Expression is InvocationExpressionSyntax awaitedInvocation)
            {
                invocation = awaitedInvocation;
            }
            else if (exprStmt.Expression is InvocationExpressionSyntax directInvocation)
            {
                invocation = directInvocation;
            }
        }

        if (invocation is null) return null;

        var symbolInfo = model.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method) return null;

        foreach (var nav in NavigationMethods)
        {
            if (method.Name == nav)
            {
                if (SymbolHelper.IsContainingTypeOneOf(method, KnownTypeNames.IPage, KnownTypeNames.IFrame)
                    || SymbolHelper.ImplementsAny(method.ContainingType, KnownTypeNames.IPage, KnownTypeNames.IFrame))
                    return nav;
            }
        }

        return null;
    }

    private static bool IsWaitStatement(StatementSyntax statement, SemanticModel model)
    {
        InvocationExpressionSyntax? invocation = null;

        if (statement is ExpressionStatementSyntax exprStmt)
        {
            if (exprStmt.Expression is AwaitExpressionSyntax awaitExpr
                && awaitExpr.Expression is InvocationExpressionSyntax awaitedInvocation)
            {
                invocation = awaitedInvocation;
            }
            else if (exprStmt.Expression is InvocationExpressionSyntax directInvocation)
            {
                invocation = directInvocation;
            }
        }

        if (invocation is null) return false;

        var symbolInfo = model.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol method) return false;

        foreach (var wait in WaitMethods)
        {
            if (method.Name == wait) return true;
        }

        return false;
    }
}
