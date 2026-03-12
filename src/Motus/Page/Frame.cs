using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Represents a frame within a page (including the main frame).
/// </summary>
internal sealed class Frame : IFrame
{
    private readonly Page _page;

    internal Frame(Page page, string id, string? parentFrameId)
    {
        _page = page;
        Id = id;
        ParentFrameId = parentFrameId;
    }

    internal string Id { get; }

    internal string? ParentFrameId { get; }

    public IPage Page => _page;

    public IFrame? ParentFrame =>
        ParentFrameId is not null && _page.TryGetFrame(ParentFrameId, out var parent)
            ? parent
            : null;

    public string Name { get; internal set; } = string.Empty;

    public string Url { get; internal set; } = string.Empty;

    public IReadOnlyList<IFrame> ChildFrames =>
        _page.GetChildFrames(Id);

    public async Task<T> EvaluateAsync<T>(string expression, object? arg = null) =>
        await _page.EvaluateInFrameAsync<T>(Id, expression, arg);

    public async Task<T> WaitForFunctionAsync<T>(string expression, object? arg = null, double? timeout = null) =>
        await _page.WaitForFunctionInFrameAsync<T>(Id, expression, arg, timeout);

    public async Task<string> ContentAsync() =>
        await EvaluateAsync<string>("document.documentElement.outerHTML");

    public async Task SetContentAsync(string html, NavigationOptions? options = null) =>
        await EvaluateAsync<object?>(
            $"document.open(); document.write({System.Text.Json.JsonSerializer.Serialize(html)}); document.close();");

    public async Task<string> TitleAsync() =>
        await EvaluateAsync<string>("document.title");

    public Task<IResponse?> GotoAsync(string url, NavigationOptions? options = null) =>
        _page.GotoAsync(url, options);

    public Task WaitForLoadStateAsync(LoadState? state = null, double? timeout = null) =>
        _page.WaitForLoadStateAsync(state, timeout);

    public Task WaitForURLAsync(string urlPattern, NavigationOptions? options = null) =>
        _page.WaitForURLAsync(urlPattern, options);

    // --- Stubbed: Phase 1I ---

    public ILocator Locator(string selector, LocatorOptions? options = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByRole(string role, string? name = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByText(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByLabel(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByPlaceholder(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByTestId(string testId)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByTitle(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public ILocator GetByAltText(string text, bool? exact = null)
        => throw new NotImplementedException("Locators are not yet implemented (Phase 1I).");

    public Task<IElementHandle> AddScriptTagAsync(string? url = null, string? content = null)
        => throw new NotImplementedException("AddScriptTag is not yet implemented (Phase 1I).");

    public Task<IElementHandle> AddStyleTagAsync(string? url = null, string? content = null)
        => throw new NotImplementedException("AddStyleTag is not yet implemented (Phase 1I).");
}
