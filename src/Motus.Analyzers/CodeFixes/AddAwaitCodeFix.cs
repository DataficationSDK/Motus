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
public sealed class AddAwaitCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.NonAwaitedCall);

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
                "Add await",
                ct => AddAwaitAsync(context.Document, invocation, ct),
                equivalenceKey: DiagnosticIds.NonAwaitedCall),
            diagnostic);
    }

    private static async Task<Document> AddAwaitAsync(
        Document document, InvocationExpressionSyntax invocation, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        // Wrap invocation in await
        var awaitExpression = SyntaxFactory.AwaitExpression(invocation.WithoutTrivia())
            .WithTriviaFrom(invocation);

        var newRoot = root.ReplaceNode(invocation, awaitExpression);

        // Check if enclosing method needs async modifier
        var method = newRoot.FindNode(invocation.Span)?.FirstAncestorOrSelf<MethodDeclarationSyntax>();
        if (method is not null && !method.Modifiers.Any(SyntaxKind.AsyncKeyword))
        {
            var asyncModifier = SyntaxFactory.Token(SyntaxKind.AsyncKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var newMethod = method.AddModifiers(asyncModifier);
            newRoot = newRoot.ReplaceNode(method, newMethod);
        }

        return document.WithSyntaxRoot(newRoot);
    }
}
