using Motus.Abstractions;

namespace Motus.Assertions;

public sealed class PageAssertions
{
    private readonly Page _page;
    private readonly bool _negate;

    internal PageAssertions(Page page, bool negate = false)
    {
        _page = page;
        _negate = negate;
    }

    public PageAssertions Not => new(_page, !_negate);

    private Task RetryAsync(
        Func<CancellationToken, Task<(bool, string)>> condition,
        string name, string expected, AssertionOptions? options) =>
        AssertionRetryHelper.RetryUntilAsync(
            condition, _negate, name, expected,
            selector: null, pageUrl: _page.Url,
            AssertionRetryHelper.ResolveTimeout(options?.Timeout),
            options?.Message, CancellationToken.None);

    public Task ToHaveUrlAsync(string urlOrGlob, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var url = _page.Url;
            var matches = Page.UrlMatchesStatic(url, urlOrGlob);
            return (matches, url);
        }, "ToHaveUrl", urlOrGlob, options);

    public Task ToHaveTitleAsync(string expected, AssertionOptions? options = null) =>
        RetryAsync(async ct =>
        {
            var title = await _page.TitleAsync().ConfigureAwait(false);
            return (title == expected, title);
        }, "ToHaveTitle", expected, options);
}
