using System.Diagnostics;
using Motus.Abstractions;

namespace Motus;

/// <summary>
/// Abstraction over process stderr for testability.
/// </summary>
internal interface IProcessStderrSource
{
    event DataReceivedEventHandler? ErrorDataReceived;
    void BeginErrorReadLine();
}

/// <summary>
/// Wraps a <see cref="Process"/> to satisfy <see cref="IProcessStderrSource"/>.
/// </summary>
internal sealed class ProcessStderrAdapter(Process process) : IProcessStderrSource
{
    public event DataReceivedEventHandler? ErrorDataReceived
    {
        add => process.ErrorDataReceived += value;
        remove => process.ErrorDataReceived -= value;
    }

    public void BeginErrorReadLine() => process.BeginErrorReadLine();
}

/// <summary>
/// Reads the Firefox process stderr to discover the WebDriver BiDi WebSocket endpoint.
/// Firefox writes: <c>WebDriver BiDi listening on ws://127.0.0.1:{port}</c>
/// </summary>
internal static class FirefoxEndpointReader
{
    private const string BiDiPrefix = "WebDriver BiDi listening on ws://";

    internal static async Task<Uri> WaitForEndpointAsync(
        IProcessStderrSource stderrSource, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<Uri>(TaskCreationOptions.RunContinuationsAsynchronously);

        stderrSource.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null && e.Data.Contains(BiDiPrefix, StringComparison.Ordinal))
            {
                var wsIndex = e.Data.IndexOf("ws://", StringComparison.Ordinal);
                if (wsIndex >= 0)
                {
                    var wsUrl = e.Data[wsIndex..];
                    tcs.TrySetResult(new Uri(wsUrl));
                }
            }
        };

        stderrSource.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);

        using var registration = timeoutCts.Token.Register(() =>
        {
            tcs.TrySetException(new MotusTimeoutException(
                timeoutDuration: timeout,
                message: $"Firefox did not provide a BiDi endpoint within {timeout.TotalSeconds}s."));
        });

        return await tcs.Task.ConfigureAwait(false);
    }
}
