using Motus.Abstractions;

namespace Motus;

internal sealed partial class Page
{
    public async Task<string> ContentAsync() =>
        await EvaluateAsync<string>("document.documentElement.outerHTML").ConfigureAwait(false);

    public async Task SetContentAsync(string html, NavigationOptions? options = null)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(html);
        await EvaluateAsync<object?>(
            $"document.open(); document.write({escaped}); document.close();").ConfigureAwait(false);
    }

    public async Task<string> TitleAsync() =>
        await EvaluateAsync<string>("document.title").ConfigureAwait(false);
}
