using Motus.Abstractions;

namespace Motus.Assertions;

/// <summary>
/// Assertions about a response the browser received, obtained from
/// <see cref="Expect.That(IResponse)"/>.
/// </summary>
/// <remarks>
/// These do not retry, unlike the locator and page assertions. A response is a completed fact
/// rather than a moving target, so re-reading its status could never change the answer.
/// </remarks>
public sealed class ResponseAssertions
{
    private readonly IResponse _response;
    private readonly bool _negate;

    internal ResponseAssertions(IResponse response, bool negate = false)
    {
        _response = response;
        _negate = negate;
    }

    /// <summary>
    /// Inverts the assertion that follows, so it passes when the condition does not hold.
    /// </summary>
    public ResponseAssertions Not => new(_response, !_negate);

    /// <summary>Asserts that the response carries a success status, meaning 200 through 299.</summary>
    /// <exception cref="MotusAssertionException">The status falls outside that range.</exception>
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

    /// <summary>Asserts that the response's status code equals the expected one.</summary>
    /// <param name="expected">The status code required, such as 404.</param>
    /// <exception cref="MotusAssertionException">The status differs from the expected one.</exception>
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
