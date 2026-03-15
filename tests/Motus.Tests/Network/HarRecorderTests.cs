namespace Motus.Tests.Network;

[TestClass]
public class HarRecorderTests
{
    private HarRecorder _recorder = null!;

    [TestInitialize]
    public void Setup()
    {
        _recorder = new HarRecorder();
        _recorder.EnableRecording();
    }

    [TestMethod]
    public void BuildHarLog_NoEvents_ReturnsEmptyEntries()
    {
        var log = _recorder.BuildHarLog();

        Assert.AreEqual("1.2", log.Version);
        Assert.AreEqual("Motus", log.Creator.Name);
        Assert.AreEqual(0, log.Entries.Length);
    }

    [TestMethod]
    public void BuildHarLog_CompleteRequest_ReturnsCorrectEntry()
    {
        var wallTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        _recorder.OnRequestWillBeSent(new NetworkRequestWillBeSentEvent(
            RequestId: "req-1",
            LoaderId: "loader-1",
            DocumentUrl: "https://example.com",
            Request: new NetworkRequestData("https://example.com/api", "GET",
                new Dictionary<string, string> { ["Accept"] = "application/json" }),
            Timestamp: 1000.0,
            WallTime: wallTime));

        _recorder.OnResponseReceived(new NetworkResponseReceivedEvent(
            RequestId: "req-1",
            LoaderId: "loader-1",
            Timestamp: 1000.5,
            Response: new NetworkResponseData(
                "https://example.com/api", 200, "OK",
                new Dictionary<string, string> { ["Content-Type"] = "application/json" },
                "application/json")));

        _recorder.OnLoadingFinished(new NetworkLoadingFinishedEvent(
            RequestId: "req-1",
            Timestamp: 1001.0,
            EncodedDataLength: 1024));

        var log = _recorder.BuildHarLog();

        Assert.AreEqual(1, log.Entries.Length);
        var entry = log.Entries[0];
        Assert.AreEqual("GET", entry.Request.Method);
        Assert.AreEqual("https://example.com/api", entry.Request.Url);
        Assert.AreEqual(200, entry.Response.Status);
        Assert.AreEqual("OK", entry.Response.StatusText);
        Assert.AreEqual("application/json", entry.Response.Content.MimeType);
        Assert.IsTrue(entry.Time > 0, "Total time should be positive");
    }

    [TestMethod]
    public void BuildHarLog_FailedRequest_RecordsFailed()
    {
        _recorder.OnRequestWillBeSent(new NetworkRequestWillBeSentEvent(
            RequestId: "req-2",
            LoaderId: "loader-1",
            DocumentUrl: "https://example.com",
            Request: new NetworkRequestData("https://example.com/fail", "POST"),
            Timestamp: 2000.0,
            WallTime: 1700000000.0));

        _recorder.OnLoadingFailed(new NetworkLoadingFailedEvent(
            RequestId: "req-2",
            Timestamp: 2001.0,
            Type: "XHR",
            ErrorText: "net::ERR_CONNECTION_REFUSED"));

        var log = _recorder.BuildHarLog();

        Assert.AreEqual(1, log.Entries.Length);
        Assert.AreEqual(0, log.Entries[0].Response.Status);
        Assert.IsTrue(log.Entries[0].Response.StatusText.Contains("ERR_CONNECTION_REFUSED"));
    }

    [TestMethod]
    public void BuildHarLog_RecordingDisabled_IgnoresEvents()
    {
        _recorder.DisableRecording();

        _recorder.OnRequestWillBeSent(new NetworkRequestWillBeSentEvent(
            RequestId: "req-3",
            LoaderId: "loader-1",
            DocumentUrl: "https://example.com",
            Request: new NetworkRequestData("https://example.com/ignored", "GET"),
            Timestamp: 3000.0,
            WallTime: 1700000000.0));

        _recorder.OnLoadingFinished(new NetworkLoadingFinishedEvent(
            RequestId: "req-3",
            Timestamp: 3001.0,
            EncodedDataLength: 512));

        var log = _recorder.BuildHarLog();
        Assert.AreEqual(0, log.Entries.Length);
    }

    [TestMethod]
    public void BuildHarLog_QueryParams_ParsedCorrectly()
    {
        _recorder.OnRequestWillBeSent(new NetworkRequestWillBeSentEvent(
            RequestId: "req-4",
            LoaderId: "loader-1",
            DocumentUrl: "https://example.com",
            Request: new NetworkRequestData("https://example.com/search?q=test&page=1", "GET"),
            Timestamp: 4000.0,
            WallTime: 1700000000.0));

        _recorder.OnResponseReceived(new NetworkResponseReceivedEvent(
            RequestId: "req-4",
            LoaderId: "loader-1",
            Timestamp: 4000.1,
            Response: new NetworkResponseData("https://example.com/search?q=test&page=1", 200, "OK")));

        _recorder.OnLoadingFinished(new NetworkLoadingFinishedEvent(
            RequestId: "req-4",
            Timestamp: 4000.2,
            EncodedDataLength: 100));

        var log = _recorder.BuildHarLog();
        var qs = log.Entries[0].Request.QueryString;
        Assert.AreEqual(2, qs.Length);
        Assert.AreEqual("q", qs[0].Name);
        Assert.AreEqual("test", qs[0].Value);
        Assert.AreEqual("page", qs[1].Name);
        Assert.AreEqual("1", qs[1].Value);
    }

    [TestMethod]
    public void BuildHarLog_WithPostData_IncludedInRequest()
    {
        _recorder.OnRequestWillBeSent(new NetworkRequestWillBeSentEvent(
            RequestId: "req-5",
            LoaderId: "loader-1",
            DocumentUrl: "https://example.com",
            Request: new NetworkRequestData("https://example.com/submit", "POST", PostData: "name=test"),
            Timestamp: 5000.0,
            WallTime: 1700000000.0));

        _recorder.OnResponseReceived(new NetworkResponseReceivedEvent(
            RequestId: "req-5",
            LoaderId: "loader-1",
            Timestamp: 5000.1,
            Response: new NetworkResponseData("https://example.com/submit", 201, "Created")));

        _recorder.OnLoadingFinished(new NetworkLoadingFinishedEvent(
            RequestId: "req-5",
            Timestamp: 5000.2,
            EncodedDataLength: 50));

        var log = _recorder.BuildHarLog();
        Assert.IsNotNull(log.Entries[0].Request.PostData);
        Assert.AreEqual("name=test", log.Entries[0].Request.PostData!.Text);
    }
}
