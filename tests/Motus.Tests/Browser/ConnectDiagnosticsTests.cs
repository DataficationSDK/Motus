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
    /// <summary>Nothing listens on port 1, so every attempt is refused straight away.</summary>
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
    public async Task Connect_WhenTheEndpointNeverAnswers_NamesTheEndpoint()
    {
        var ex = await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await MotusLauncher.ConnectAsync(DeadEndpoint, new ConnectOptions { Timeout = 500 }));

        StringAssert.Contains(ex.Message, "never answered with a WebSocket URL");
        StringAssert.Contains(ex.Message, "The last attempt to reach it said");
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
