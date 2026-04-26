using System;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Detects a <c>sourceMappingURL</c> reference in a JavaScript or CSS asset and
/// returns the parsed source map. Inline base64 data URIs are decoded directly;
/// http(s) and file URLs are fetched via <see cref="SourceMapFetcher"/>.
/// Returns null on any failure so the caller can fall back to generated-file coverage.
/// </summary>
internal sealed class SourceMapResolver
{
    private const int TailScanBytes = 2048;

    private static readonly Regex JsCommentRegex = new(
        @"//[#@]\s*sourceMappingURL=([^\s'""]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CssCommentRegex = new(
        @"/\*[#@]\s*sourceMappingURL=([^\s'""\*]+)\s*\*/",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly SourceMapFetcher _fetcher;
    private readonly IMotusLogger? _logger;

    public SourceMapResolver(SourceMapFetcher fetcher, IMotusLogger? logger = null)
    {
        _fetcher = fetcher;
        _logger = logger;
    }

    /// <summary>
    /// Try to resolve the source map for an asset.
    /// </summary>
    /// <param name="source">Full asset source text.</param>
    /// <param name="assetUrl">Origin URL of the asset (http, https, or file).</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Parsed map, or null if no reference was found, the URL was rejected, or parsing failed.</returns>
    public async Task<SourceMap?> TryResolveAsync(string source, string assetUrl, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(source))
            return null;

        var mapUrl = ExtractMapReference(source);
        if (mapUrl is null)
            return null;

        try
        {
            string? json;
            if (mapUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                json = DecodeDataUri(mapUrl);
            }
            else
            {
                if (!Uri.TryCreate(assetUrl, UriKind.Absolute, out var assetUri))
                    return null;
                if (!Uri.TryCreate(assetUri, mapUrl, out var resolvedMapUri))
                    return null;

                json = await _fetcher.FetchAsync(resolvedMapUri, assetUri, ct).ConfigureAwait(false);
            }

            if (json is null)
                return null;

            return SourceMapParser.Parse(json);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning($"Failed to resolve source map for '{assetUrl}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Scan the trailing portion of the asset for a sourceMappingURL reference and
    /// return the URL string, or null if none was found within the tail window.
    /// </summary>
    internal static string? ExtractMapReference(string source)
    {
        int start = Math.Max(0, source.Length - TailScanBytes);
        var tail = source.AsSpan(start).ToString();

        var jsMatch = JsCommentRegex.Match(tail);
        if (jsMatch.Success)
            return jsMatch.Groups[1].Value;

        var cssMatch = CssCommentRegex.Match(tail);
        if (cssMatch.Success)
            return cssMatch.Groups[1].Value;

        return null;
    }

    private static string? DecodeDataUri(string dataUri)
    {
        const string base64Marker = ";base64,";
        int base64Idx = dataUri.IndexOf(base64Marker, StringComparison.OrdinalIgnoreCase);
        if (base64Idx < 0)
        {
            int commaIdx = dataUri.IndexOf(',');
            if (commaIdx < 0) return null;
            return Uri.UnescapeDataString(dataUri.Substring(commaIdx + 1));
        }

        var base64 = dataUri.Substring(base64Idx + base64Marker.Length);
        try
        {
            var bytes = Convert.FromBase64String(base64);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return null;
        }
    }
}
