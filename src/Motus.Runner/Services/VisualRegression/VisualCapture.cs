namespace Motus.Runner.Services.VisualRegression;

public sealed record VisualCapture(
    string TestName,
    string CaptureName,
    byte[] Screenshot,
    byte[]? Baseline,
    DiffResult? Diff);
