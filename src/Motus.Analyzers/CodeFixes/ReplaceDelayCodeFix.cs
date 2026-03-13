using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Motus.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class ReplaceDelayCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.HardcodedDelay);

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var span = diagnostic.Location.SourceSpan;

        var invocation = root.FindNode(span)?.FirstAncestorOrSelf<InvocationExpressionSyntax>();
        if (invocation is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Replace with WaitForLoadStateAsync",
                ct => ReplaceWithWaitAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.HardcodedDelay),
            diagnostic);
    }

    private static async Task<Document> ReplaceWithWaitAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // Best-effort: find a `page` variable in scope
        var pageName = FindPageVariable(invocation) ?? "page";

        // Build: page.WaitForLoadStateAsync(LoadState.NetworkIdle)
        var replacement = SyntaxFactory.InvocationExpression(
            SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(pageName),
                SyntaxFactory.IdentifierName("WaitForLoadStateAsync")),
            SyntaxFactory.ArgumentList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("LoadState"),
                            SyntaxFactory.IdentifierName("NetworkIdle"))))));

        // Wrap in await expression
        ExpressionSyntax finalExpr = SyntaxFactory.AwaitExpression(replacement);

        // Find the statement that needs replacing
        var nodeToReplace = invocation.FirstAncestorOrSelf<ExpressionStatementSyntax>();
        if (nodeToReplace is null)
        {
            // Fallback: just replace the invocation
            var newRoot = root.ReplaceNode(invocation, finalExpr.WithTriviaFrom(invocation));
            return document.WithSyntaxRoot(newRoot);
        }

        // If the invocation was already awaited, unwrap
        if (nodeToReplace.Expression is AwaitExpressionSyntax)
        {
            // Already awaited, just replace the whole statement
        }

        var newStatement = SyntaxFactory.ExpressionStatement(finalExpr)
            .WithTriviaFrom(nodeToReplace);
        var newRootNode = root.ReplaceNode(nodeToReplace, newStatement);

        // Add async modifier to enclosing method if missing
        var method = newRootNode.FindNode(nodeToReplace.Span)?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is not null && !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            var asyncModifier = SyntaxFactory.Token(SyntaxKind.AsyncKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var newMethod = method.AddModifiers(asyncModifier);
            newRootNode = newRootNode.ReplaceNode(method, newMethod);
        }

        return document.WithSyntaxRoot(newRootNode);
    }

    private static string? FindPageVariable(SyntaxNode node)
    {
        // Walk up to the method body and search for IPage variables
        var method = node.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method?.Body is null) return null;

        foreach (var statement in method.Body.Statements)
        {
            if (statement is LocalDeclarationStatementSyntax localDecl)
            {
                foreach (var variable in localDecl.Declaration.Variables)
                {
                    var typeName = localDecl.Declaration.Type.ToString();
                    if (typeName.Contains("IPage") || typeName.Contains("Page"))
                        return variable.Identifier.Text;
                }
            }
        }

        // Check parameters
        if (method.ParameterList is not null)
        {
            foreach (var param in method.ParameterList.Parameters)
            {
                var typeName = param.Type?.ToString() ?? "";
                if (typeName.Contains("IPage") || typeName.Contains("Page"))
                    return param.Identifier.Text;
            }
        }

        return null;
    }
}
