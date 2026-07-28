namespace Motus.Abstractions;

/// <summary>
/// Options for evaluating JavaScript.
/// </summary>
public sealed record EvaluateOptions
{
    /// <summary>
    /// Which JavaScript world to evaluate in. Defaults to the page's own world, so an expression
    /// reads what the application sees unless told otherwise.
    /// </summary>
    public ExecutionWorld World { get; init; } = ExecutionWorld.Main;
}
