using System.Runtime.ExceptionServices;
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
        return configTimeout ?? 10_000;
    }

    internal static async Task RetryUntilAsync(
        Func<CancellationToken, Task<(bool passed, string actual)>> condition,
        bool negate, string assertionName, string expected,
        string? selector, string? pageUrl,
        int timeoutMs, string? customMessage, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMs));

        const string NeverEvaluated = "<not evaluated>";

        var linkedToken = cts.Token;
        string lastActual = NeverEvaluated;

        // Kept so a poll that only ever failed can still say why. Without it the reason is
        // discarded on every iteration and the timeout is all that survives.
        Exception? lastRetriedError = null;

        try
        {
            while (true)
            {
                linkedToken.ThrowIfCancellationRequested();

                try
                {
                    var (passed, actual) = await condition(linkedToken).ConfigureAwait(false);
                    lastActual = actual;

                    var effective = negate ? !passed : passed;
                    if (effective)
                        return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is InvalidOperationException   // evaluation failures, element not found
                    or Abstractions.MotusProtocolException  // CDP command errors (stale context, etc.)
                    or Abstractions.MotusTargetClosedException  // target navigated away
                    or TimeoutException)               // inner operation timeouts
                {
                    // Retriable error from element resolution or JS evaluation; retry
                    lastRetriedError = ex;
                }

                await Task.Delay(PollingIntervalMs, linkedToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // A target that went away is retried above, because during a navigation the old
            // execution context dies and the next poll succeeds against the new one. When the
            // browser itself is gone no poll ever succeeds, so the condition is never evaluated
            // and the loop runs out the clock. Reporting that as an assertion failure states a
            // verdict about the page that was never reached, and it hides the loss from
            // BrowserFailure.IsBrowserLost, which is what both retry paths ask. So when nothing
            // was ever evaluated and the last error was the target closing, that error is what
            // happened and it is what propagates.
            if (lastActual == NeverEvaluated && BrowserFailure.IsBrowserLost(lastRetriedError))
                ExceptionDispatchInfo.Capture(lastRetriedError!).Throw();

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
