namespace Motus.Mcp;

/// <summary>
/// What the server does with a JavaScript dialog the page raises.
/// </summary>
/// <remarks>
/// A dialog blocks the page until it is answered, so the choice is between letting the agent decide
/// and answering on its behalf. <see cref="Ask"/> is the default because the answer is part of what
/// the agent is testing. The other two exist for unattended runs, where nobody is there to answer
/// and a page that stops on a confirm is a run that stops.
/// </remarks>
public enum DialogPolicy
{
    /// <summary>
    /// Hold the dialog open and report it, so a later <c>handle_dialog</c> call decides the answer.
    /// The action that opened it returns straight away rather than waiting on a blocked page.
    /// </summary>
    Ask,

    /// <summary>Accept every dialog as soon as it opens, as clicking OK would.</summary>
    Accept,

    /// <summary>Dismiss every dialog as soon as it opens, as clicking Cancel would.</summary>
    Dismiss,
}
