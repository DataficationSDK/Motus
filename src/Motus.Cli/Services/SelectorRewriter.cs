using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Motus.Cli.Services;

/// <summary>
/// Summary of a <see cref="SelectorRewriter"/> pass.
/// </summary>
internal sealed record RewriteReport(int FilesModified, int FixesApplied, int FixesAttempted);

/// <summary>
/// Applies High-confidence repair suggestions to their source files using Roslyn.
/// Mirrors the <c>root.ReplaceNode</c> pattern used by the Motus.Analyzers CodeFixes
/// rather than subclassing <see cref="CSharpSyntaxRewriter"/>: each broken locator
/// call is located by line + method + argument and replaced in a single batch
/// <see cref="SyntaxNode.ReplaceNodes{TNode}"/> pass per file.
/// </summary>
internal static class SelectorRewriter
{
    /// <summary>
    /// Applies repairs in <paramref name="results"/> where the top suggestion is
    /// <see cref="Confidence.High"/>. Mutates each modified <see cref="SelectorCheckResult"/>
    /// by populating <c>Fixed</c>, <c>AppliedSuggestion</c>, or <c>FixError</c>.
    /// </summary>
    internal static RewriteReport Apply(
        List<SelectorCheckResult> results,
        bool backup,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(results);

        var targetsByFile = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var attempted = 0;
        for (var i = 0; i < results.Count; i++)
        {
            var r = results[i];
            if (r.Status != SelectorCheckStatus.Broken)
                continue;
            if (r.Suggestions is null || r.Suggestions.Count == 0)
                continue;
            if (r.Suggestions[0].Confidence != Confidence.High)
                continue;
            if (r.Fixed)
                continue;

            if (!targetsByFile.TryGetValue(r.SourceFile, out var indices))
            {
                indices = new List<int>();
                targetsByFile[r.SourceFile] = indices;
            }
            indices.Add(i);
            attempted++;
        }

        if (targetsByFile.Count == 0)
            return new RewriteReport(0, 0, 0);

        var filesModified = 0;
        var fixesApplied = 0;

        foreach (var (file, indices) in targetsByFile)
        {
            ct.ThrowIfCancellationRequested();
            var modified = TryRewriteFile(file, indices, results, backup, ct);
            if (modified > 0)
            {
                filesModified++;
                fixesApplied += modified;
            }
        }

        return new RewriteReport(filesModified, fixesApplied, attempted);
    }

    private static int TryRewriteFile(
        string file,
        List<int> resultIndices,
        List<SelectorCheckResult> results,
        bool backup,
        CancellationToken ct)
    {
        string source;
        try
        {
            source = File.ReadAllText(file);
        }
        catch (Exception ex)
        {
            foreach (var idx in resultIndices)
                results[idx] = results[idx] with { FixError = $"read failed: {ex.Message}" };
            return 0;
        }

        var tree = CSharpSyntaxTree.ParseText(source, path: file);
        var root = tree.GetRoot();

        var pairs = new Dictionary<SyntaxNode, SyntaxNode>();
        var appliedByIndex = new Dictionary<int, string>();

        foreach (var idx in resultIndices)
        {
            var r = results[idx];
            var suggestion = r.Suggestions![0].Replacement;

            var target = FindInvocation(root, r);
            if (target is null)
            {
                results[idx] = r with { FixError = "source invocation not found" };
                continue;
            }

            SyntaxNode replacement;
            try
            {
                replacement = BuildReplacement(target, suggestion);
            }
            catch (Exception ex)
            {
                results[idx] = r with { FixError = $"could not parse suggestion: {ex.Message}" };
                continue;
            }

            if (pairs.ContainsKey(target))
            {
                results[idx] = r with { FixError = "duplicate rewrite target in same file" };
                continue;
            }

            pairs[target] = replacement;
            appliedByIndex[idx] = suggestion;
        }

        if (pairs.Count == 0)
            return 0;

        var newRoot = root.ReplaceNodes(pairs.Keys, (original, _) => pairs[original]);
        var newText = newRoot.ToFullString();

        try
        {
            if (backup)
                File.Copy(file, file + ".bak", overwrite: true);
            File.WriteAllText(file, newText);
        }
        catch (Exception ex)
        {
            foreach (var idx in appliedByIndex.Keys)
                results[idx] = results[idx] with { FixError = $"write failed: {ex.Message}" };
            return 0;
        }

        foreach (var (idx, applied) in appliedByIndex)
        {
            results[idx] = results[idx] with
            {
                Fixed = true,
                AppliedSuggestion = applied,
                FixError = null,
            };
        }

        return appliedByIndex.Count;
    }

    private static InvocationExpressionSyntax? FindInvocation(SyntaxNode root, SelectorCheckResult r)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
                continue;
            if (member.Name.Identifier.ValueText != r.LocatorMethod)
                continue;

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            if (line != r.SourceLine)
                continue;

            if (invocation.ArgumentList.Arguments.Count == 0)
                continue;

            var firstArg = invocation.ArgumentList.Arguments[0].Expression;
            var argText = firstArg switch
            {
                LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression)
                    => literal.Token.ValueText,
                MemberAccessExpressionSyntax m => m.ToString(),
                InvocationExpressionSyntax nameofInvoke
                    when nameofInvoke.Expression is IdentifierNameSyntax id && id.Identifier.ValueText == "nameof"
                    => ExtractNameofValue(nameofInvoke) ?? "",
                _ => null,
            };

            if (argText is not null && argText == r.Selector)
                return invocation;
        }
        return null;
    }

    private static string? ExtractNameofValue(InvocationExpressionSyntax nameofInvocation)
    {
        if (nameofInvocation.ArgumentList.Arguments.Count != 1)
            return null;
        return nameofInvocation.ArgumentList.Arguments[0].Expression switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            _ => null,
        };
    }

    /// <summary>
    /// Builds a replacement <see cref="InvocationExpressionSyntax"/> preserving the
    /// original receiver expression (left side of the dot) and the original
    /// leading/trailing trivia. Parses the suggestion as <c>__r.X(...)</c> so the
    /// result is a single invocation with a well-formed argument list.
    /// </summary>
    private static InvocationExpressionSyntax BuildReplacement(
        InvocationExpressionSyntax original, string suggestion)
    {
        var parsed = SyntaxFactory.ParseExpression("__motus_r." + suggestion);
        if (parsed is not InvocationExpressionSyntax parsedInvocation)
            throw new InvalidOperationException("suggestion did not parse as an invocation expression");
        if (parsedInvocation.Expression is not MemberAccessExpressionSyntax parsedMember)
            throw new InvalidOperationException("suggestion did not parse as a member-access invocation");
        if (original.Expression is not MemberAccessExpressionSyntax originalMember)
            throw new InvalidOperationException("original invocation was not a member-access");

        var newMember = originalMember.WithName(parsedMember.Name);
        return SyntaxFactory.InvocationExpression(newMember, parsedInvocation.ArgumentList)
            .WithTriviaFrom(original);
    }
}
