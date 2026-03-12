using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Built-in data-testid selector strategy. Prefix: data-testid=
/// </summary>
internal sealed class TestIdSelectorStrategy : ISelectorStrategy
{
    private readonly string _attributeName;
    private readonly bool _pierceShadow;

    internal TestIdSelectorStrategy(string attributeName = "data-testid", bool pierceShadow = false)
    {
        _attributeName = attributeName;
        _pierceShadow = pierceShadow;
    }

    public string StrategyName => _attributeName;

    public int Priority => 40;

    public async Task<IReadOnlyList<IElementHandle>> ResolveAsync(
        string selector, IFrame frame, CancellationToken ct = default)
    {
        var page = SelectorStrategyHelpers.GetPage(frame);
        var escapedAttr = JsonEncodedText.Encode(_attributeName).ToString();
        var escapedVal = JsonEncodedText.Encode(selector).ToString();
        var js = $"""Array.from(document.querySelectorAll('[{escapedAttr}="{escapedVal}"]'))""";
        return await SelectorStrategyHelpers.EvalToHandlesAsync(page, js, ct);
    }

    public async Task<string?> GenerateSelector(IElementHandle element, CancellationToken ct = default)
    {
        var value = await element.GetAttributeAsync(_attributeName, ct);
        return value is not null ? $"{_attributeName}={value}" : null;
    }
}
