using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in text selector strategy. Prefix: text=
/// Supports exact match with double quotes: text="exact text"
/// </summary>
internal sealed class TextSelectorStrategy : ISelectorStrategy
{
    private readonly bool _pierceShadow;

    internal TextSelectorStrategy(bool pierceShadow = false)
    {
        _pierceShadow = pierceShadow;
    }

    public string StrategyName => "text";

    public int Priority => 20;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);

        var isExact = selector.Length >= 2 && selector[0] == '"' && selector[^1] == '"';
        var text = isExact ? selector[1..^1] : selector;
        var escaped = JsonEncodedText.Encode(text).ToString();
        var matchExpr = isExact
            ? $"""el.textContent&&el.textContent.trim()==="{escaped}" """
            : $"""el.textContent&&el.textContent.includes("{escaped}")""";

        var js = $$"""
            (()=>{
                var results=[];
                var walker=document.createTreeWalker(document.body,NodeFilter.SHOW_ELEMENT);
                var el;
                while(el=walker.nextNode()){
                    if({{matchExpr}}) results.push(el);
                }
                return results;
            })()
            """;

        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct);
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var text = await element.TextContentAsync(ct);
        if (text is null || text.Length > 100)
            return null;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return null;

        return "text=" + trimmed;
    }
}
