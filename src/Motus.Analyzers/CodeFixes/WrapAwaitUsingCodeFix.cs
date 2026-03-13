using System.Collections.Immutable;
using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Motus.Analyzers.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp), Shared]
public sealed class WrapAwaitUsingCodeFix : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds =>
        ImmutableArray.Create(DiagnosticIds.MissingDisposal);

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root is null) return;

        var diagnostic = context.Diagnostics[0];
        var span = diagnostic.Location.SourceSpan;

        var localDecl = root.FindNode(span)?.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
        if (localDecl is null) return;

        context.RegisterCodeFix(
            CodeAction.Create(
                "Add await using",
                ct => AddAwaitUsingAsync(context.Document, localDecl, ct),
                equivalenceKey: DiagnosticIds.MissingDisposal),
            diagnostic);
    }

    private static async Task<Document> AddAwaitUsingAsync(
        Document document, LocalDeclarationStatementSyntax localDecl, CancellationToken ct)
    {
        var root = await document.GetSyntaxRootAsync(ct).ConfigureAwait(false);
        if (root is null) return document;

        var newDecl = localDecl;

        // Add await keyword if missing
        if (newDecl.AwaitKeyword == default)
        {
            newDecl = newDecl.WithAwaitKeyword(
                SyntaxFactory.Token(SyntaxKind.AwaitKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space));
        }

        // Add using keyword if missing
        if (newDecl.UsingKeyword == default)
        {
            newDecl = newDecl.WithUsingKeyword(
                SyntaxFactory.Token(SyntaxKind.UsingKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space));
        }

        var newRoot = root.ReplaceNode(localDecl, newDecl);
        return document.WithSyntaxRoot(newRoot);
    }
}
