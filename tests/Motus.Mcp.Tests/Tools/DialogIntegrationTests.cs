using System.Diagnostics;
using ModelContextProtocol.Protocol;
using Motus.Abstractions;
using Motus.Mcp;
using Motus.Mcp.Tests.Fixtures;

namespace Motus.Mcp.Tests.Tools;

/// <summary>
/// Clicking a button whose handler calls <c>alert()</c>, against a real browser. The browser stops
/// answering input the moment the dialog goes up, so the command that dispatched the click is left
/// unanswered and the tool call that would report the dialog is the one stuck behind it. This is
/// the test that the call comes back instead, and says what happened.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public class DialogIntegrationTests
{
    /// <summary>
    /// Generous next to the tenth of a second the click should take, and far below the minute the
    /// transport waits before giving up on a command the browser never answered.
    /// </summary>
    private static readonly TimeSpan ReturnsPromptly = TimeSpan.FromSeconds(2);

    private McpBenchFixtureServer? _server;
    private BrowserSessionManager? _sessions;
    private DialogService? _dialogs;
    private ActivePageService? _pages;

    [TestInitialize]
    public async Task Setup()
    {
        _server = new McpBenchFixtureServer();
        _sessions = new BrowserSessionManager(new McpServerLaunchOptions { Headless = true });
        _dialogs = new DialogService();
        _pages = new ActivePageService(_sessions, _dialogs);

        try
        {
            var page = await _pages.GetOrCreateActivePageAsync();
            await page.GotoAsync(_server.IndexUrl);
        }
        catch (FileNotFoundException)
        {
            Assert.Inconclusive("No browser found; skipping integration test.");
        }
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        _pages?.Shutdown();
        if (_sessions is not null)
            await _sessions.DisposeAsync();

        _server?.Dispose();
    }

    [TestMethod]
    public async Task Click_OnAButtonThatOpensAnAlert_ReturnsAtOnce_AndHandleDialogLetsThePageFinish()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;

        var snapshot = await SnapshotTextAsync();
        var deleteRef = RefForLineContaining(snapshot, "Delete account");

        var stopwatch = Stopwatch.StartNew();
        var click = await CoreTools.ClickAsync(deleteRef, pages, ct, @double: null);
        stopwatch.Stop();

        Assert.IsTrue(
            stopwatch.Elapsed < ReturnsPromptly,
            $"the click took {stopwatch.Elapsed.TotalSeconds:F1} s; it should not wait on a blocked page.");
        StringAssert.Contains(TextOf(click), "alert dialog");
        StringAssert.Contains(TextOf(click), "Are you sure?");
        StringAssert.Contains(TextOf(click), "handle_dialog");

        // Anything else the agent tries now says why the page is not moving, without waiting on a
        // renderer that has stopped answering, and leaves the dialog where handle_dialog finds it.
        stopwatch.Restart();
        var blocked = DialogNotice.Prefix(_dialogs, await CoreTools.SnapshotAsync(pages, ct, null, null));
        stopwatch.Stop();

        Assert.IsTrue(
            stopwatch.Elapsed < ReturnsPromptly,
            $"the snapshot took {stopwatch.Elapsed.TotalSeconds:F1} s behind an open dialog.");
        StringAssert.Contains(TextOf(blocked), "dialog is open");
        StringAssert.Contains(TextOf(blocked), "handle_dialog");

        var handled = await PageTools.HandleDialogAsync(accept: true, _dialogs!, ct, text: null);
        Assert.IsFalse(handled.IsError ?? false, TextOf(handled));
        StringAssert.Contains(TextOf(handled), "accepted");

        // The handler carries on past the alert once it is answered, so the page reaches the state
        // the click was for.
        var after = await WaitForStatusAsync("after alert");
        Assert.IsTrue(after, "the page should have reached its post-alert state.");

        var freed = DialogNotice.Prefix(_dialogs, await CoreTools.SnapshotAsync(pages, ct, null, null));
        Assert.IsFalse(TextOf(freed).Contains("dialog is open", StringComparison.Ordinal),
            "no dialog is open any more, so nothing should say one is.");
        StringAssert.Contains(TextOf(freed), "after alert");
    }

    [TestMethod]
    public async Task Click_WithTheAcceptPolicy_NeverStopsToAsk()
    {
        var pages = _pages!;
        var ct = CancellationToken.None;
        _dialogs!.Policy = DialogPolicy.Accept;

        var deleteRef = RefForLineContaining(await SnapshotTextAsync(), "Delete account");

        var stopwatch = Stopwatch.StartNew();
        var click = await CoreTools.ClickAsync(deleteRef, pages, ct, @double: null);
        stopwatch.Stop();

        Assert.IsTrue(
            stopwatch.Elapsed < ReturnsPromptly,
            $"the click took {stopwatch.Elapsed.TotalSeconds:F1} s with the dialog answered for it.");
        Assert.IsFalse(click.IsError ?? false, TextOf(click));
        StringAssert.Contains(TextOf(click), "Clicked");
        Assert.IsNull(_dialogs.PeekPendingDialog(), "an accepted dialog is not waiting for anyone.");
        Assert.IsTrue(await WaitForStatusAsync("after alert"));
    }

    private async Task<string> SnapshotTextAsync()
    {
        var snapshot = await CoreTools.SnapshotAsync(_pages!, CancellationToken.None, null, null);
        Assert.IsFalse(snapshot.IsError ?? false, TextOf(snapshot));
        return TextOf(snapshot);
    }

    /// <summary>
    /// The status paragraph is filled in by a handler, so it lands a moment after the dialog is
    /// answered rather than as part of answering it.
    /// </summary>
    private async Task<bool> WaitForStatusAsync(string expected)
    {
        var page = await _pages!.GetOrCreateActivePageAsync();
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var status = await page.EvaluateAsync<string>(
                "document.getElementById('status').textContent");
            if (status == expected)
                return true;

            await Task.Delay(50);
        }

        return false;
    }

    private static string TextOf(CallToolResult result)
        => string.Join("\n", result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    private static string RefForLineContaining(string snapshot, string needle)
    {
        foreach (var line in snapshot.Split('\n'))
        {
            if (!line.Contains(needle, StringComparison.Ordinal))
                continue;

            var start = line.IndexOf("[ref=", StringComparison.Ordinal);
            if (start < 0)
                continue;

            var end = line.IndexOf(']', start);
            return line[(start + "[ref=".Length)..end];
        }

        Assert.Fail($"No ref for a line containing '{needle}' in:\n{snapshot}");
        return string.Empty;
    }
}
