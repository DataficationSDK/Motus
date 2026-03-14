namespace Motus.Runner.Services.Models;

public sealed record TestNodeState(string FullName, TestStatus Status, TimeSpan? Duration, string? ErrorMessage, string? StackTrace);
