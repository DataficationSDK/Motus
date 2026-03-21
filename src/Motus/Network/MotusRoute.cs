using Motus.Abstractions;

namespace Motus;

internal sealed class MotusRoute : IRoute
{
    private readonly IMotusSession _session;
    private readonly string _fetchRequestId;
    private bool _handled;

    internal MotusRoute(IRequest request, IMotusSession session, string fetchRequestId)
    {
        Request = request;
        _session = session;
        _fetchRequestId = fetchRequestId;
    }

    public IRequest Request { get; }

    internal bool IsHandled => _handled;

    public async Task FulfillAsync(RouteFulfillOptions? options = null)
    {
        ThrowIfHandled();
        _handled = true;

        byte[] bodyBytes = options switch
        {
            { BodyBytes: { } bb } => bb,
            { Body: { } s } => System.Text.Encoding.UTF8.GetBytes(s),
            { Path: { } p } => await File.ReadAllBytesAsync(p).ConfigureAwait(false),
            _ => []
        };

        var headers = BuildFetchHeaders(options?.Headers, options?.ContentType);

        await _session.SendAsync(
            "Fetch.fulfillRequest",
            new FetchFulfillRequestParams(
                RequestId: _fetchRequestId,
                ResponseCode: options?.Status ?? 200,
                ResponseHeaders: headers.Length > 0 ? headers : null,
                Body: bodyBytes.Length > 0 ? Convert.ToBase64String(bodyBytes) : null),
            CdpJsonContext.Default.FetchFulfillRequestParams,
            CdpJsonContext.Default.FetchFulfillRequestResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task ContinueAsync(RouteContinueOptions? options = null)
    {
        ThrowIfHandled();
        _handled = true;

        FetchHeaderEntry[]? headers = options?.Headers is not null
            ? HeaderCollection.ToFetchHeaders(options.Headers)
            : null;

        string? postData = options?.PostData is not null
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(options.PostData))
            : null;

        await _session.SendAsync(
            "Fetch.continueRequest",
            new FetchContinueRequestParams(
                RequestId: _fetchRequestId,
                Url: options?.Url,
                Method: options?.Method,
                PostData: postData,
                Headers: headers),
            CdpJsonContext.Default.FetchContinueRequestParams,
            CdpJsonContext.Default.FetchContinueRequestResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    public async Task AbortAsync(string? errorCode = null)
    {
        ThrowIfHandled();
        _handled = true;

        await _session.SendAsync(
            "Fetch.failRequest",
            new FetchFailRequestParams(
                RequestId: _fetchRequestId,
                ErrorReason: MapErrorCode(errorCode ?? "failed")),
            CdpJsonContext.Default.FetchFailRequestParams,
            CdpJsonContext.Default.FetchFailRequestResult,
            CancellationToken.None).ConfigureAwait(false);
    }

    private void ThrowIfHandled()
    {
        if (_handled)
            throw new InvalidOperationException("Route has already been handled.");
    }

    private static FetchHeaderEntry[] BuildFetchHeaders(
        IDictionary<string, string>? headers,
        string? contentType)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (var kv in headers)
                result[kv.Key] = kv.Value;
        }
        if (contentType is not null)
            result["Content-Type"] = contentType;
        return HeaderCollection.ToFetchHeaders(result);
    }

    private static string MapErrorCode(string code) => code.ToLowerInvariant() switch
    {
        "aborted" => "Aborted",
        "accessdenied" => "AccessDenied",
        "connectionrefused" => "ConnectionRefused",
        "connectionreset" => "ConnectionReset",
        "timedout" => "TimedOut",
        "connectionfailed" => "ConnectionFailed",
        "namenotresolved" => "NameNotResolved",
        "internetdisconnected" => "InternetDisconnected",
        "addressunreachable" => "AddressUnreachable",
        "blockedbyclient" => "BlockedByClient",
        "blockedbyresponse" => "BlockedByResponse",
        _ => "Failed"
    };
}
