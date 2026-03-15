namespace Motus;

/// <summary>
/// Opt-in HAR 1.2 recorder that captures network events forwarded from NetworkManager.
/// </summary>
internal sealed class HarRecorder
{
    private readonly Dictionary<string, HarEntryBuilder> _builders = new();
    private readonly List<HarEntry> _completedEntries = [];
    private readonly object _lock = new();
    private bool _recording;

    internal void EnableRecording() => _recording = true;

    internal void DisableRecording() => _recording = false;

    internal bool IsRecording => _recording;

    internal void OnRequestWillBeSent(NetworkRequestWillBeSentEvent evt)
    {
        if (!_recording) return;

        lock (_lock)
        {
            _builders[evt.RequestId] = new HarEntryBuilder
            {
                RequestId = evt.RequestId,
                Method = evt.Request.Method,
                Url = evt.Request.Url,
                RequestHeaders = evt.Request.Headers,
                PostData = evt.Request.PostData,
                WallTime = evt.WallTime,
                StartTimestamp = evt.Timestamp,
            };
        }
    }

    internal void OnResponseReceived(NetworkResponseReceivedEvent evt)
    {
        if (!_recording) return;
        if (evt.Response is null) return;

        lock (_lock)
        {
            if (!_builders.TryGetValue(evt.RequestId, out var builder))
                return;

            builder.Status = evt.Response.Status;
            builder.StatusText = evt.Response.StatusText;
            builder.ResponseHeaders = evt.Response.Headers;
            builder.MimeType = evt.Response.MimeType;
            builder.ResponseTimestamp = evt.Timestamp;
        }
    }

    internal void OnLoadingFinished(NetworkLoadingFinishedEvent evt)
    {
        if (!_recording) return;

        lock (_lock)
        {
            if (!_builders.TryGetValue(evt.RequestId, out var builder))
                return;

            builder.EndTimestamp = evt.Timestamp;
            builder.EncodedDataLength = evt.EncodedDataLength;
            _completedEntries.Add(builder.Build());
            _builders.Remove(evt.RequestId);
        }
    }

    internal void OnLoadingFailed(NetworkLoadingFailedEvent evt)
    {
        if (!_recording) return;

        lock (_lock)
        {
            if (!_builders.TryGetValue(evt.RequestId, out var builder))
                return;

            builder.EndTimestamp = evt.Timestamp;
            builder.Failed = true;
            builder.ErrorText = evt.ErrorText;
            _completedEntries.Add(builder.Build());
            _builders.Remove(evt.RequestId);
        }
    }

    internal HarLog BuildHarLog()
    {
        List<HarEntry> entries;
        lock (_lock)
            entries = _completedEntries.ToList();

        return new HarLog(
            Version: "1.2",
            Creator: new HarCreator("Motus", "1.0"),
            Pages: [],
            Entries: entries.ToArray());
    }

    private sealed class HarEntryBuilder
    {
        internal string RequestId { get; set; } = "";
        internal string Method { get; set; } = "GET";
        internal string Url { get; set; } = "";
        internal Dictionary<string, string>? RequestHeaders { get; set; }
        internal string? PostData { get; set; }
        internal double WallTime { get; set; }
        internal double StartTimestamp { get; set; }
        internal double? ResponseTimestamp { get; set; }
        internal double? EndTimestamp { get; set; }
        internal int Status { get; set; }
        internal string StatusText { get; set; } = "";
        internal Dictionary<string, string>? ResponseHeaders { get; set; }
        internal string? MimeType { get; set; }
        internal double EncodedDataLength { get; set; }
        internal bool Failed { get; set; }
        internal string? ErrorText { get; set; }

        internal HarEntry Build()
        {
            var startedDateTime = DateTimeOffset
                .FromUnixTimeMilliseconds((long)(WallTime * 1000))
                .ToString("o");

            var totalTime = (EndTimestamp ?? StartTimestamp) - StartTimestamp;
            var waitTime = (ResponseTimestamp ?? StartTimestamp) - StartTimestamp;
            var receiveTime = totalTime - waitTime;

            var requestHeaders = RequestHeaders?
                .Select(kv => new HarHeader(kv.Key, kv.Value)).ToArray() ?? [];

            var responseHeaders = ResponseHeaders?
                .Select(kv => new HarHeader(kv.Key, kv.Value)).ToArray() ?? [];

            var queryParams = ParseQueryParams(Url);

            HarPostData? postData = null;
            if (PostData is not null)
                postData = new HarPostData("application/x-www-form-urlencoded", PostData);

            return new HarEntry(
                StartedDateTime: startedDateTime,
                Time: totalTime * 1000,
                Request: new HarRequest(
                    Method: Method,
                    Url: Url,
                    HttpVersion: "HTTP/1.1",
                    Headers: requestHeaders,
                    QueryString: queryParams,
                    HeadersSize: -1,
                    BodySize: PostData?.Length ?? -1,
                    PostData: postData),
                Response: new HarResponse(
                    Status: Failed ? 0 : Status,
                    StatusText: Failed ? (ErrorText ?? "Failed") : StatusText,
                    HttpVersion: "HTTP/1.1",
                    Headers: responseHeaders,
                    Content: new HarContent(
                        Size: (int)EncodedDataLength,
                        MimeType: MimeType ?? "application/octet-stream"),
                    HeadersSize: -1,
                    BodySize: (int)EncodedDataLength),
                Timings: new HarTimings(
                    Send: 0,
                    Wait: waitTime * 1000,
                    Receive: receiveTime * 1000));
        }

        private static HarQueryParam[] ParseQueryParams(string url)
        {
            var idx = url.IndexOf('?');
            if (idx < 0) return [];

            var query = url[(idx + 1)..];
            var ampIdx = query.IndexOf('#');
            if (ampIdx >= 0) query = query[..ampIdx];

            return query.Split('&', StringSplitOptions.RemoveEmptyEntries)
                .Select(p =>
                {
                    var eqIdx = p.IndexOf('=');
                    return eqIdx >= 0
                        ? new HarQueryParam(Uri.UnescapeDataString(p[..eqIdx]), Uri.UnescapeDataString(p[(eqIdx + 1)..]))
                        : new HarQueryParam(Uri.UnescapeDataString(p), "");
                }).ToArray();
        }
    }
}
