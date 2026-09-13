using Motus.Abstractions;

namespace Motus.Mcp;

/// <summary>
/// How a result names a page: its address, and what the page calls itself when it says so.
/// </summary>
/// <remarks>
/// A URL alone often does not tell an agent which page it is looking at, since an application
/// that routes on a path or a query string can answer with anything. The title is what the page
/// says it is, it is one short read, and it is the first thing that tells the agent whether the
/// navigation went where it meant.
/// </remarks>
internal static class PageDescription
{
    /// <summary>
    /// How long to wait for a page to say what it is called.
    /// </summary>
    /// <remarks>
    /// Reading the title is a request to the renderer, and a renderer busy with a long script
    /// answers it when it is done. Every action reads the title, before and after, so an unbounded
    /// read would put the page's own state in front of the tool call: waiting a minute to find out
    /// what a page is called is exactly the behaviour the dialog work exists to prevent. The title
    /// is a nicety, so it is given a short wait and left out when it does not arrive.
    /// </remarks>
    private static readonly TimeSpan TitleTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Renders a page as <c>url | title</c>, or as the URL alone when it has no title.</summary>
    public static string Of(string url, string? title)
        => string.IsNullOrEmpty(title) ? url : $"{url} | {title}";

    /// <summary>Renders the page as it is now, reading its title.</summary>
    public static async Task<string> OfAsync(IPage page)
        => Of(page.Url, await TryTitleAsync(page).ConfigureAwait(false));

    /// <summary>
    /// The page's title, or null when it will not say within <see cref="TitleTimeout"/>. A page
    /// that cannot be asked is described by its address alone rather than failing, or delaying, the
    /// call that was only naming it.
    /// </summary>
    public static async Task<string?> TryTitleAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Task<string> reading;
        try
        {
            reading = page.TitleAsync();
        }
        catch (Exception)
        {
            return null;
        }

        using var expiry = new CancellationTokenSource();
        var waited = Task.Delay(TitleTimeout, expiry.Token);

        if (await Task.WhenAny(reading, waited).ConfigureAwait(false) != reading)
        {
            // The read is left to finish in its own time, with its outcome observed so a failure
            // is not raised later against some unrelated call.
            _ = reading.ContinueWith(
                static finished => _ = finished.Exception,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);

            return null;
        }

        await expiry.CancelAsync().ConfigureAwait(false);

        try
        {
            return await reading.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
