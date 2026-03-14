using System.Text;
using System.Text.Json;
using Motus.Abstractions;

namespace Motus;

internal sealed class MotusResponse : IResponse
{
    private readonly CdpSession _session;
    private readonly string _networkRequestId;
    private byte[]? _body;

    internal MotusResponse(
        string url,
        int status,
        string statusText,
        Dictionary<string, string>? headers,
        IRequest request,
        IFrame frame,
        CdpSession session,
        string networkRequestId)
    {
        Url = url;
        Status = status;
        StatusText = statusText;
        Headers = new HeaderCollection(headers);
        Request = request;
        Frame = frame;
        _session = session;
        _networkRequestId = networkRequestId;
    }

    public string Url { get; }
    public int Status { get; }
    public string StatusText { get; }
    public IHeaderCollection Headers { get; }
    public bool Ok => Status >= 200 && Status < 300;
    public IRequest Request { get; }
    public IFrame Frame { get; }

    public async Task<byte[]> BodyAsync(CancellationToken ct = default)
    {
        if (_body is not null)
            return _body;

        var result = await _session.SendAsync(
            "Network.getResponseBody",
            new NetworkGetResponseBodyParams(_networkRequestId),
            CdpJsonContext.Default.NetworkGetResponseBodyParams,
            CdpJsonContext.Default.NetworkGetResponseBodyResult,
            ct).ConfigureAwait(false);

        _body = result.Base64Encoded
            ? Convert.FromBase64String(result.Body)
            : Encoding.UTF8.GetBytes(result.Body);

        return _body;
    }

    public async Task<string> TextAsync(CancellationToken ct = default) =>
        Encoding.UTF8.GetString(await BodyAsync(ct).ConfigureAwait(false));

    public async Task<T> JsonAsync<T>(CancellationToken ct = default) =>
        JsonSerializer.Deserialize<T>(await TextAsync(ct).ConfigureAwait(false))!;
}
