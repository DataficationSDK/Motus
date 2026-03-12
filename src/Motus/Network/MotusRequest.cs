using Motus.Abstractions;

namespace Motus;

internal sealed class MotusRequest : IRequest
{
    private MotusResponse? _response;

    internal MotusRequest(
        string url,
        string method,
        Dictionary<string, string>? headers,
        string? postData,
        string resourceType,
        bool isNavigationRequest,
        IFrame frame)
    {
        Url = url;
        Method = method;
        Headers = new HeaderCollection(headers);
        PostData = postData;
        ResourceType = resourceType;
        IsNavigationRequest = isNavigationRequest;
        Frame = frame;
    }

    public string Url { get; }
    public string Method { get; }
    public IHeaderCollection Headers { get; }
    public string? PostData { get; }
    public string ResourceType { get; }
    public bool IsNavigationRequest { get; }
    public IFrame Frame { get; }
    public IResponse? Response => _response;

    internal void SetResponse(MotusResponse response) => _response = response;
}
