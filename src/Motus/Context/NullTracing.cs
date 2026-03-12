using Motus.Abstractions;

namespace Motus;

internal sealed class NullTracing : ITracing
{
    internal static NullTracing Instance { get; } = new();

    private NullTracing() { }

    public Task StartAsync(TracingStartOptions? options = null) => Task.CompletedTask;

    public Task StopAsync(TracingStopOptions? options = null) => Task.CompletedTask;
}
