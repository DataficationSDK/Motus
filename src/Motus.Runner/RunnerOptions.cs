namespace Motus.Runner;

public sealed class RunnerOptions
{
    public string[] AssemblyPaths { get; set; } = [];
    public string? Filter { get; set; }
    public int Port { get; set; } = 5100;
    public string? BaselinePath { get; set; }
    public ViewerMode ViewerMode { get; set; } = ViewerMode.Runner;
    public string? TraceFilePath { get; set; }
    public string? TrxFilePath { get; set; }
    public bool RepairMode { get; set; }
}
