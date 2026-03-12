using Motus.Abstractions;

namespace Motus.Assertions;

public sealed class ResponseAssertions
{
    private readonly IResponse _response;
    private readonly bool _negate;

    internal ResponseAssertions(IResponse response, bool negate = false)
    {
        _response = response;
        _negate = negate;
    }

    public ResponseAssertions Not => new(_response, !_negate);

    public Task ToBeOkAsync()
    {
        var ok = _response.Ok;
        var pass = _negate ? !ok : ok;

        if (!pass)
        {
            var negateLabel = _negate ? "NOT " : "";
            throw new MotusAssertionException(
                expected: $"{negateLabel}OK (200-299)",
                actual: _response.Status.ToString(),
                selector: null,
                pageUrl: _response.Url,
                assertionTimeout: TimeSpan.Zero,
                message: $"Response assertion {negateLabel}ToBeOk failed. Status: {_response.Status}. URL: {_response.Url}.");
        }

        return Task.CompletedTask;
    }

    public Task ToHaveStatusAsync(int expected)
    {
        var match = _response.Status == expected;
        var pass = _negate ? !match : match;

        if (!pass)
        {
            var negateLabel = _negate ? "NOT " : "";
            throw new MotusAssertionException(
                expected: $"{negateLabel}{expected}",
                actual: _response.Status.ToString(),
                selector: null,
                pageUrl: _response.Url,
                assertionTimeout: TimeSpan.Zero,
                message: $"Response assertion {negateLabel}ToHaveStatus failed. Expected: {negateLabel}{expected}. Received: {_response.Status}. URL: {_response.Url}.");
        }

        return Task.CompletedTask;
    }
}
