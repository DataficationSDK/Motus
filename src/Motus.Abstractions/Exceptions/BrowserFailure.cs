namespace Motus.Abstractions;

/// <summary>
/// Tells failures caused by the browser going away from failures the test found.
/// </summary>
public static class BrowserFailure
{
    /// <summary>
    /// True when the exception says the connection to the browser was lost.
    /// </summary>
    /// <remarks>
    /// A test that ended this way reached no verdict about the page: it was interrupted, and what
    /// it reports is the connection closing rather than anything it set out to check. That is the
    /// one kind of failure worth running again, and the reason it is worth telling apart from an
    /// assertion that simply did not hold.
    /// </remarks>
    public static bool IsBrowserLost(Exception? exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is MotusTargetClosedException)
                return true;
        }

        return false;
    }
}
