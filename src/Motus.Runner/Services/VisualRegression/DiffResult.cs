namespace Motus.Runner.Services.VisualRegression;

public sealed record DiffResult(bool IsMatch, double DiffPercent, int DiffPixelCount, byte[]? DiffImage);
