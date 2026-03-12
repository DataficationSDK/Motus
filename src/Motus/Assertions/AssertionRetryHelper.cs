using Motus.Abstractions;

namespace Motus.Assertions;

internal static class AssertionRetryHelper
{
    private const int PollingIntervalMs = 100;

    internal static int ResolveTimeout(int? perCallTimeout)
    {
        if (perCallTimeout.HasValue)
            return perCallTimeout.Value;

        var configTimeout = MotusConfigLoader.Config.Assertions?.Timeout;
        return configTimeout ?? 30_000;
    }

    internal static async Task RetryUntilAsync(
        Func<CancellationToken, Task<(bool passed, string actual)>> condition,
        bool negate, string assertionName, string expected,
        string? selector, string? pageUrl,
        int timeoutMs, string? customMessage, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        var linkedToken = cts.Token;
        string lastActual = "<not evaluated>";

        try
        {
            while (true)
            {
                linkedToken.ThrowIfCancellationRequested();

                try
                {
                    var (passed, actual) = await condition(linkedToken);
                    lastActual = actual;

                    var effective = negate ? !passed : passed;
                    if (effective)
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Element not found or evaluation error; retry
                }

                await Task.Delay(PollingIntervalMs, linkedToken);
            }
        }
        catch (OperationCanceledException)
        {
            var negateLabel = negate ? "NOT " : "";
            var message = customMessage
                ?? $"Assertion {negateLabel}{assertionName} failed after {timeoutMs}ms."
                   + $" Expected: {negateLabel}{expected}. Received: {lastActual}."
                   + (selector is not null ? $" Selector: {selector}." : "")
                   + (pageUrl is not null ? $" Page: {pageUrl}." : "");

            throw new MotusAssertionException(
                expected: $"{negateLabel}{expected}",
                actual: lastActual,
                selector: selector,
                pageUrl: pageUrl,
                assertionTimeout: TimeSpan.FromMilliseconds(timeoutMs),
                message: message);
        }
    }
}
