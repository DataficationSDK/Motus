namespace Motus.Mcp;

/// <summary>
/// Description text that more than one tool shows to the model, kept in one place so a parameter
/// meaning the same thing everywhere reads the same way everywhere.
/// </summary>
/// <remarks>
/// The descriptions are the only documentation an agent has at the moment it picks a tool, so two
/// tools describing the same argument differently is a reason for it to believe they behave
/// differently. The values are compile-time constants because they are used in attributes.
/// </remarks>
internal static class ToolDescriptions
{
    /// <summary>
    /// How every tool describes the element it acts on. The prefixes are the ones the engine
    /// registers a selector strategy for; anything without a prefix is read as CSS.
    /// </summary>
    public const string Target =
        "A ref from the latest snapshot (e5, or f1e5 for an element inside frame 1), or a selector: "
        + "CSS by default, or prefixed with xpath=, text=, role=, or data-testid=. A selector needs "
        + "no snapshot.";
}
