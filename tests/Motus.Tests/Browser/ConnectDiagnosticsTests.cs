using Motus.Abstractions;

namespace Motus.Tests.Browser;

/// <summary>
/// What a connection that runs out of time says about itself.
/// </summary>
/// <remarks>
/// Connecting is three waits one after another, and a single message covering all three leaves a
/// caller unable to tell a browser that never opened its debugging port from one that opened it
/// and then stopped answering. These pin that each wait names itself.
/// </remarks>
[TestClass]
public class ConnectDiagnosticsTests
{
    /// <summary>
    /// Nothing listens on port 1, so every attempt is refused. How quickly is the operating
    /// system's call: Windows takes about a second over a refused loopback connection, so a wait
    /// with a shorter budget can run out before its first attempt has finished failing.
    /// </summary>
    private const string DeadEndpoint = "http://127.0.0.1:1";

    private static readonly TimeSpan PollBudget = TimeSpan.FromMilliseconds(400);

    [TestMethod]
    public async Task WaitForEndpoint_SaysWhyTheLastAttemptFailed()
    {
        var poller = new CdpEndpointPoller();

        var ex = await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await poller.WaitForEndpointAsync(new Uri(DeadEndpoint), PollBudget, CancellationToken.None));

        Assert.IsNotNull(poller.LastAttemptError, "the poller kept nothing about why the attempts failed");
        StringAssert.Contains(ex.Message, "The last attempt to reach it said");
        StringAssert.Contains(ex.Message, poller.LastAttemptError.Message);
    }

    [TestMethod]
    public async Task Connect_WhenTheEndpointNeverOffersAWebSocketUrl_SaysWhatItAnsweredInstead()
    {
        // Something is listening, so every attempt completes on every platform, and it is not a
        // browser, so none of them produces a WebSocket URL. The dead endpoint above would exercise
        // the same wait, but whether one of its attempts has failed by the time the budget runs out
        // depends on the platform, and the message can only name a failure it has seen.
        using var endpoint = new StallingCdpEndpoint(completeHandshake: false, offersWebSocketUrl: false);

        var ex = await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await MotusLauncher.ConnectAsync(endpoint.HttpEndpoint, new ConnectOptions { Timeout = 1500 }));

        StringAssert.Contains(ex.Message, "never answered with a WebSocket URL");
        StringAssert.Contains(ex.Message, "The last attempt to reach it said");
        StringAssert.Contains(ex.Message, "answered without a webSocketDebuggerUrl");
    }

    [TestMethod]
    public async Task Connect_WhenTheWebSocketNeverOpens_NamesTheWebSocket()
    {
        using var endpoint = new StallingCdpEndpoint(completeHandshake: false);

        var ex = await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await MotusLauncher.ConnectAsync(endpoint.HttpEndpoint, new ConnectOptions { Timeout = 1500 }));

        StringAssert.Contains(ex.Message, "never finished opening");
    }

    [TestMethod]
    public async Task Connect_WhenTheBrowserNeverAnswers_NamesWhatItWasWaitingFor()
    {
        using var endpoint = new StallingCdpEndpoint(completeHandshake: true);

        var ex = await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await MotusLauncher.ConnectAsync(endpoint.HttpEndpoint, new ConnectOptions { Timeout = 1500 }));

        StringAssert.Contains(ex.Message, "taking over the tabs already open");
    }
}
