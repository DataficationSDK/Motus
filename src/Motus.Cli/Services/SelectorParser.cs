using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.FileSystemGlobbing.Abstractions;

namespace Motus.Cli.Services;

/// <summary>
/// Statically parses Motus locator calls from C# source files using Roslyn,
/// without requiring a live browser session. Used by selector-maintenance
/// commands (e.g. <c>motus check-selectors</c>) to discover the selectors
/// referenced by a test suite.
/// </summary>
internal static class SelectorParser
{
    private static readonly HashSet<string> LocatorMethods = new(StringComparer.Ordinal)
    {
        "Locator",
        "GetByRole",
        "GetByText",
        "GetByTestId",
        "GetByLabel",
        "GetByPlaceholder",
        "GetByAltText",
        "GetByTitle",
    };

    /// <summary>
    /// Parses a single C# source string and returns every Motus locator call found.
    /// </summary>
    /// <param name="source">The C# source text.</param>
    /// <param name="sourceFile">
    /// Path to associate with each <see cref="ParsedSelector"/> (recorded verbatim).
    /// Pass an absolute path so downstream consumers don't need to re-resolve.
    /// </param>
    internal static SelectorParseResult ParseSource(string source, string sourceFile)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);

        var selectors = new List<ParsedSelector>();
        var warnings = new List<SelectorParseWarning>();

        var tree = CSharpSyntaxTree.ParseText(source, path: sourceFile);
        var root = tree.GetRoot();

        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
                continue;

            var methodName = member.Name.Identifier.ValueText;
            if (!LocatorMethods.Contains(methodName))
                continue;

            var args = invocation.ArgumentList.Arguments;
            if (args.Count == 0)
                continue;

            var line = invocation.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            var firstArg = args[0].Expression;

            switch (firstArg)
            {
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression):
                    selectors.Add(new ParsedSelector(
                        literal.Token.ValueText, methodName, sourceFile, line, IsInterpolated: false));
                    break;

                case InvocationExpressionSyntax nameofInvoke when IsNameofInvocation(nameofInvoke):
                    var nameofValue = ExtractNameofValue(nameofInvoke);
                    if (nameofValue is null)
                    {
                        warnings.Add(new SelectorParseWarning(
                            sourceFile, line,
                            $"selector argument to '{methodName}' uses an unrecognized 'nameof' form."));
                    }
                    else
                    {
                        selectors.Add(new ParsedSelector(
                            nameofValue, methodName, sourceFile, line, IsInterpolated: false));
                    }
                    break;

                case InterpolatedStringExpressionSyntax interpolated:
                    var template = RenderInterpolationTemplate(interpolated);
                    selectors.Add(new ParsedSelector(
                        template, methodName, sourceFile, line, IsInterpolated: true));
                    warnings.Add(new SelectorParseWarning(
                        sourceFile, line,
                        $"selector argument to '{methodName}' is an interpolated string and cannot be validated statically."));
                    break;

                case MemberAccessExpressionSyntax memberArg:
                    selectors.Add(new ParsedSelector(
                        memberArg.ToString(), methodName, sourceFile, line, IsInterpolated: false));
                    break;

                default:
                    warnings.Add(new SelectorParseWarning(
                        sourceFile, line,
                        $"selector argument to '{methodName}' is not a static expression and was skipped."));
                    break;
            }
        }

        return new SelectorParseResult(selectors, warnings);
    }

    /// <summary>
    /// Resolves <paramref name="globPattern"/> against <paramref name="baseDirectory"/>,
    /// reads each matching <c>.cs</c> file, and aggregates the parse results.
    /// </summary>
    internal static async Task<SelectorParseResult> ParseGlobAsync(
        string globPattern, string baseDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(globPattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        if (!Directory.Exists(baseDirectory))
            return new SelectorParseResult(Array.Empty<ParsedSelector>(), Array.Empty<SelectorParseWarning>());

        var matcher = new Matcher();
        matcher.AddInclude(globPattern);

        var directoryInfo = new DirectoryInfoWrapper(new DirectoryInfo(baseDirectory));
        var match = matcher.Execute(directoryInfo);

        var selectors = new List<ParsedSelector>();
        var warnings = new List<SelectorParseWarning>();

        foreach (var file in match.Files)
        {
            ct.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, file.Path));
            var source = await File.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            var result = ParseSource(source, fullPath);

            selectors.AddRange(result.Selectors);
            warnings.AddRange(result.Warnings);
        }

        return new SelectorParseResult(selectors, warnings);
    }

    private static bool IsNameofInvocation(InvocationExpressionSyntax invocation) =>
        invocation.Expression is IdentifierNameSyntax id
        && id.Identifier.ValueText == "nameof"
        && invocation.ArgumentList.Arguments.Count == 1;

    private static string? ExtractNameofValue(InvocationExpressionSyntax nameofInvocation)
    {
        var arg = nameofInvocation.ArgumentList.Arguments[0].Expression;
        return arg switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
            _ => null,
        };
    }

    private static string RenderInterpolationTemplate(InterpolatedStringExpressionSyntax interpolated)
    {
        var buffer = new System.Text.StringBuilder();
        foreach (var content in interpolated.Contents)
        {
            switch (content)
            {
                case InterpolatedStringTextSyntax text:
                    buffer.Append(text.TextToken.ValueText);
                    break;
                case InterpolationSyntax:
                    buffer.Append("{…}");
                    break;
            }
        }
        return buffer.ToString();
    }
}
