using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Motus;

/// <summary>
/// Loads source-map text from an http(s) URL or file path. Origin-locked when the
/// asset URL is itself http(s): map URLs from a different origin are refused.
/// </summary>
internal sealed class SourceMapFetcher
{
    private static readonly HttpClient SharedClient = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Motus/1.0");
        return client;
    }

    /// <summary>
    /// Fetch the map document at <paramref name="mapUri"/> in the context of an asset
    /// loaded from <paramref name="assetUri"/>. Returns null if the fetch is refused
    /// (cross-origin) or fails (timeout, non-2xx, file missing).
    /// </summary>
    public async Task<string?> FetchAsync(Uri mapUri, Uri assetUri, CancellationToken ct)
    {
        if (mapUri.IsFile)
        {
            try
            {
                return await File.ReadAllTextAsync(mapUri.LocalPath, ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        if (mapUri.Scheme != Uri.UriSchemeHttp && mapUri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (!IsSameOrigin(mapUri, assetUri))
            return null;

        try
        {
            using var resp = await SharedClient.GetAsync(mapUri, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return null;
            return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameOrigin(Uri a, Uri b)
    {
        if (!a.IsAbsoluteUri || !b.IsAbsoluteUri) return false;
        return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
            && a.Port == b.Port;
    }
}
