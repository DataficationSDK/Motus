namespace Motus.Tests.Browser;

[TestClass]
public class BrowserOutputDrainTests
{
    [TestMethod]
    public async Task Drain_ReadsAStreamToItsEnd()
    {
        var stream = new StringReader("DevTools listening on ws://127.0.0.1:9222/devtools/browser/abc");

        var drain = BrowserOutputDrain.Start(stream);

        await WaitForTailAsync(drain, "ws://127.0.0.1:9222");
    }

    [TestMethod]
    public async Task Drain_ReadsEveryStreamItIsGiven()
    {
        var drain = BrowserOutputDrain.Start(new StringReader("from stdout"), new StringReader("from stderr"));

        await WaitForTailAsync(drain, "from stdout");
        await WaitForTailAsync(drain, "from stderr");
    }

    [TestMethod]
    public async Task Drain_KeepsReadingPastWhatItRetains()
    {
        // The point of draining is that the browser is never blocked writing, however much it
        // writes. Only the tail is kept, but everything is read.
        var written = new string('x', 40 * 1024) + "the last thing it said";
        var stream = new StringReader(written);

        var drain = BrowserOutputDrain.Start(stream);

        await WaitForTailAsync(drain, "the last thing it said");
        Assert.IsTrue(drain.Tail.Length < written.Length, "the whole of a long stream should not be retained");
    }

    [TestMethod]
    public async Task Drain_SurvivesAStreamThatFails()
    {
        var failing = new FailingReader();
        var drain = BrowserOutputDrain.Start(failing, new StringReader("the other stream is still read"));

        await WaitForTailAsync(drain, "the other stream is still read");
    }

    [TestMethod]
    public void Describe_SaysNothingWhenTheBrowserWasSilent()
    {
        var drain = BrowserOutputDrain.Start(new StringReader(string.Empty));

        Assert.AreEqual(string.Empty, drain.Describe());
    }

    [TestMethod]
    public async Task Describe_CarriesWhatTheBrowserWrote()
    {
        var drain = BrowserOutputDrain.Start(new StringReader("Failed to create a temporary profile"));

        await WaitForTailAsync(drain, "Failed to create a temporary profile");
        StringAssert.Contains(drain.Describe(), "Failed to create a temporary profile");
    }

    /// <summary>
    /// Draining runs on its own, so a test waits for what it is looking for rather than assuming
    /// it has already arrived.
    /// </summary>
    private static async Task WaitForTailAsync(BrowserOutputDrain drain, string expected)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTime.UtcNow < deadline)
        {
            if (drain.Tail.Contains(expected, StringComparison.Ordinal))
                return;

            await Task.Delay(10);
        }

        Assert.Fail($"Drained output never contained '{expected}'. It held: {drain.Tail}");
    }

    private sealed class FailingReader : TextReader
    {
        public override int Read(char[] buffer, int index, int count)
            => throw new IOException("the browser took its stream with it");
    }
}
