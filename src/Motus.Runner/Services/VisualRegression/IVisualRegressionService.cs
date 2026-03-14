using Motus.Abstractions;

namespace Motus.Runner.Services.VisualRegression;

public interface IVisualRegressionService
{
    IReadOnlyList<VisualCapture> AllCaptures { get; }
    event Action? CapturesChanged;
    Task<VisualCapture> CaptureAsync(IPage page, string testName, string captureName, CancellationToken ct = default);
    Task AcceptBaselineAsync(string testName, string captureName, byte[] screenshot);
    void Reject(string testName, string captureName);
}
