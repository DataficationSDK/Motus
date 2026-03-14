namespace Motus.Runner;

public sealed class RunnerOptions
{
    public string[] AssemblyPaths { get; set; } = [];
    public string? Filter { get; set; }
    public int Port { get; set; } = 5100;
}
