using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in data-testid selector strategy. Prefix: data-testid=
/// </summary>
internal sealed class TestIdSelectorStrategy : ISelectorStrategy
{
    private readonly string _attributeName;

    internal TestIdSelectorStrategy(string attributeName = "data-testid")
    {
        _attributeName = attributeName;
    }

    public string StrategyName => _attributeName;

    public int Priority => 40;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, bool pierceShadow = true, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var escapedAttr = JsonEncodedText.Encode(_attributeName).ToString();
        var escapedVal = JsonEncodedText.Encode(selector).ToString();
        var cssSelector = $"""[{escapedAttr}="{escapedVal}"]""";

        var js = pierceShadow
            ? $$"""
                (()=>{
                    function queryShadow(root,sel){
                        var results=Array.from(root.querySelectorAll(sel));
                        var all=root.querySelectorAll('*');
                        for(var i=0;i<all.length;i++){
                            var sr=all[i].shadowRoot;
                            if(sr) results=results.concat(queryShadow(sr,sel));
                        }
                        return results;
                    }
                    return queryShadow(document,'{{cssSelector}}');
                })()
                """
            : $"""Array.from(document.querySelectorAll('{cssSelector}'))""";

        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct);
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var value = await element.GetAttributeAsync(_attributeName, ct);
        return value is not null ? $"{_attributeName}={value}" : null;
    }
}
