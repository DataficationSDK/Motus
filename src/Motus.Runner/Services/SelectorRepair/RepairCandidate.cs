namespace Motus.Runner.Services.SelectorRepair;

public sealed record RepairCandidate(
    string Replacement,
    string StrategyName,
    RepairConfidence Confidence);
