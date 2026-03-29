using System.Diagnostics;
using Motus.Abstractions;

namespace Motus.Tests.Browser;

/// <summary>
/// Fake stderr source for testing <see cref="FirefoxEndpointReader"/> without a real process.
/// </summary>
internal sealed class FakeProcessStderrSource : IProcessStderrSource
{
    public event DataReceivedEventHandler? ErrorDataReceived;

    private readonly string[] _lines;
    private readonly TimeSpan _delayPerLine;

    internal FakeProcessStderrSource(string[] lines, TimeSpan? delayPerLine = null)
    {
        _lines = lines;
        _delayPerLine = delayPerLine ?? TimeSpan.Zero;
    }

    public void BeginErrorReadLine()
    {
        // Fire lines on a background thread to simulate async stderr output
        _ = Task.Run(async () =>
        {
            foreach (var line in _lines)
            {
                if (_delayPerLine > TimeSpan.Zero)
                    await Task.Delay(_delayPerLine);

                ErrorDataReceived?.Invoke(this,
                    CreateDataReceivedEventArgs(line));
            }
        });
    }

    private static DataReceivedEventArgs CreateDataReceivedEventArgs(string data)
    {
        // DataReceivedEventArgs has no public constructor.
        // Use reflection-free approach: create via Activator with internal constructor.
        // Since DataReceivedEventArgs is sealed with internal constructor in .NET,
        // we use a workaround: create via System.Runtime.Serialization.
        var args = (DataReceivedEventArgs)System.Runtime.CompilerServices.RuntimeHelpers
            .GetUninitializedObject(typeof(DataReceivedEventArgs));

        // Set the _data field
        var field = typeof(DataReceivedEventArgs).GetField("_data",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(args, data);

        return args;
    }
}

[TestClass]
public class FirefoxEndpointReaderTests
{
    [TestMethod]
    public async Task WaitForEndpointAsync_ParsesExpectedLine_ReturnsUri()
    {
        var stderr = new FakeProcessStderrSource([
            "GLib-GIO-Message: Some startup message",
            "WebDriver BiDi listening on ws://127.0.0.1:9222",
            "Some other output"
        ]);

        var result = await FirefoxEndpointReader.WaitForEndpointAsync(
            stderr, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.AreEqual("ws://127.0.0.1:9222/session", result.ToString());
        Assert.AreEqual(9222, result.Port);
    }

    [TestMethod]
    public async Task WaitForEndpointAsync_ParsesLineWithPath_ReturnsUri()
    {
        var stderr = new FakeProcessStderrSource([
            "WebDriver BiDi listening on ws://127.0.0.1:4444/session"
        ]);

        var result = await FirefoxEndpointReader.WaitForEndpointAsync(
            stderr, TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.AreEqual("ws://127.0.0.1:4444/session", result.ToString());
        Assert.AreEqual(4444, result.Port);
    }

    [TestMethod]
    public async Task WaitForEndpointAsync_ThrowsTimeout_WhenNoLine()
    {
        var stderr = new FakeProcessStderrSource([
            "Some irrelevant output",
            "More irrelevant output"
        ]);

        var ex = await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await FirefoxEndpointReader.WaitForEndpointAsync(
                stderr, TimeSpan.FromMilliseconds(200), CancellationToken.None));

        StringAssert.Contains(ex.Message, "BiDi endpoint");
    }

    [TestMethod]
    public async Task WaitForEndpointAsync_Cancellation_Throws()
    {
        var stderr = new FakeProcessStderrSource([], TimeSpan.FromSeconds(10));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsExceptionAsync<MotusTimeoutException>(async () =>
            await FirefoxEndpointReader.WaitForEndpointAsync(
                stderr, TimeSpan.FromMilliseconds(100), cts.Token));
    }
}
