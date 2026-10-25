using Microsoft.Extensions.Logging.Abstractions;
using Motus.Abstractions;
using Motus.Runner.Services;
using Motus.Runner.Services.Models;
using Motus.Runner.Services.Timeline;

namespace Motus.Tests.Runner;

/// <summary>
/// The visual runner runs tests in a test host process of their own assembly, so the browser view,
/// the timeline and step debugging all have to reach it from there.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class VisualTestHostIntegrationTests
{
    [TestMethod]
    public async Task BrowserTest_InATestHost_StreamsFramesTimelineAndSteps()
    {
        var timeline = new TimelineService();
        var screencast = new ScreencastService();
        var stepDebug = new CountingStepDebug();
        var discovery = new TestDiscovery(NullLogger<TestDiscovery>.Instance);
        var executor = new TestExecutionService(NullLogger<TestExecutionService>.Instance, null, screencast, timeline, stepDebug);
        var session = new TestSessionService(discovery, executor, timeline);

        var frames = 0;
        screencast.FrameReceived += _ => Interlocked.Increment(ref frames);

        await session.LoadAssembliesAsync([typeof(VisualTestHostFixture).Assembly.Location], nameof(VisualTestHostFixture));
        var test = session.DiscoveredTests.Single();
        Assert.IsNull(test.TestClass, "a discovered test should run in a test host");

        await session.RunAllAsync();

        var state = session.States[test.FullName];
        if (state.Status == TestStatus.Failed && state.ErrorMessage?.Contains("No browser found", StringComparison.Ordinal) == true)
            Assert.Inconclusive("No browser found; skipping integration tests.");

        Assert.AreEqual(TestStatus.Passed, state.Status, state.ErrorMessage);
        Assert.IsTrue(frames > 0, "the browser view should receive the test host's screencast");
        Assert.IsTrue(timeline.Entries.Any(e => e.ActionType == "click" && e.TestName == test.FullName),
            "the timeline should record the click under the test that made it: "
            + string.Join(", ", timeline.Entries.Select(e => $"{e.ActionType} ({e.TestName})")));
        CollectionAssert.AreEqual(
            Enumerable.Range(0, timeline.Entries.Count).ToList(),
            timeline.Entries.Select(e => e.Index).ToList(),
            "entries from the test host are numbered in the order they arrive");
        Assert.IsTrue(stepDebug.Actions.Contains("click"), "each action should ask the step debugger before it runs");
        Assert.IsFalse(screencast.IsStreaming, "the browser view should stop once the test host has gone");
    }

    private sealed class CountingStepDebug : IStepDebugService
    {
        public List<string> Actions { get; } = [];
        public bool IsStepMode => false;
        public bool IsPaused => false;
        public string? PendingActionType => null;
        public string? PendingSelector => null;
        public event Action? StateChanged { add { } remove { } }
        public void EnableStepMode() { }
        public void DisableStepMode() { }
        public void Advance() { }
        public void Resume() { }

        public Task WaitIfPausedAsync(string actionType, string? selector, CancellationToken ct)
        {
            lock (Actions)
                Actions.Add(actionType);
            return Task.CompletedTask;
        }
    }
}

/// <summary>A browser test that the visual runner runs in a test host in the test above.</summary>
[TestClass]
[TestCategory("Integration")]
public class VisualTestHostFixture
{
    [TestMethod]
    public async Task ClicksAButton()
    {
        IBrowser browser;
        try
        {
            browser = await MotusLauncher.LaunchAsync(new LaunchOptions { Headless = true });
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration tests.");
            return;
        }

        await using (browser)
        {
            var page = await browser.NewPageAsync();
            await page.SetContentAsync(
                "<style>@keyframes spin{to{transform:rotate(360deg)}}div{width:40px;height:40px;background:teal;animation:spin 1s linear infinite}</style>"
                + "<div></div><button onclick=\"this.textContent='done'\">go</button>");
            await page.Locator("button").ClickAsync();

            // Long enough for the screencast to send frames of the animation.
            await Task.Delay(1000);
        }
    }
}
