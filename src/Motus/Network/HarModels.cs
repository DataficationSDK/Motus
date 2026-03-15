namespace Motus;

/// <summary>HAR 1.2 model records for trace export serialization.</summary>

internal sealed record HarLog(
    string Version,
    HarCreator Creator,
    HarPage[] Pages,
    HarEntry[] Entries);

internal sealed record HarCreator(string Name, string Version);

internal sealed record HarPage(
    string StartedDateTime,
    string Id,
    string Title);

internal sealed record HarEntry(
    string StartedDateTime,
    double Time,
    HarRequest Request,
    HarResponse Response,
    HarTimings Timings,
    string? ServerIPAddress = null,
    string? PageRef = null);

internal sealed record HarRequest(
    string Method,
    string Url,
    string HttpVersion,
    HarHeader[] Headers,
    HarQueryParam[] QueryString,
    int HeadersSize,
    int BodySize,
    HarPostData? PostData = null);

internal sealed record HarResponse(
    int Status,
    string StatusText,
    string HttpVersion,
    HarHeader[] Headers,
    HarContent Content,
    int HeadersSize,
    int BodySize,
    string? RedirectURL = null);

internal sealed record HarHeader(string Name, string Value);

internal sealed record HarQueryParam(string Name, string Value);

internal sealed record HarPostData(string MimeType, string Text);

internal sealed record HarContent(
    int Size,
    string MimeType,
    string? Text = null);

internal sealed record HarTimings(
    double Send,
    double Wait,
    double Receive);
