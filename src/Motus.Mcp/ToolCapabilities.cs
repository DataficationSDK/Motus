namespace Motus.Mcp;

/// <summary>
/// The optional tool groups a server can be asked to add to its catalog, and the names that
/// ask for them.
/// </summary>
/// <remarks>
/// Every tool the catalog advertises is described to the client on connection, whether or not
/// the agent ever calls it, so the catalog is a standing cost in every conversation. The groups
/// here are the ones a session either leans on throughout or never touches at all, which makes
/// them worth naming: a session that reads and clicks its way through a site has no use for
/// coordinate input, request mocking, or a trace file, and pays nothing for them unless it asks.
/// Adding a group never removes anything, so the default catalog is a floor rather than a
/// starting point to be pared back.
/// </remarks>
public static class ToolCapabilities
{
    /// <summary>Coordinate input and viewport sizing, for surfaces the accessibility tree cannot address.</summary>
    public const string Coordinates = "coordinates";

    /// <summary>Traces, HAR files, and video recording.</summary>
    public const string Recording = "recording";

    /// <summary>Isolated browser contexts, each with its own cookies and storage.</summary>
    public const string Contexts = "contexts";

    /// <summary>Request mocking: fulfilling, blocking, and overriding requests on the active context.</summary>
    public const string Routing = "routing";

    /// <summary>
    /// Every group name, in the order the documentation lists them. Kept in one place so the
    /// command line's help text, its error message, and the registration below cannot disagree.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = [Coordinates, Recording, Contexts, Routing];

    /// <summary>
    /// Whether <paramref name="name"/> is one of the group names, ignoring case and surrounding
    /// whitespace.
    /// </summary>
    public static bool IsKnown(string? name)
        => name is not null && All.Contains(name.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the given selection asks for <paramref name="group"/>. A null or empty selection
    /// asks for nothing, which is the default catalog.
    /// </summary>
    internal static bool Includes(IReadOnlyList<string>? selection, string group)
    {
        if (selection is null)
            return false;

        for (var i = 0; i < selection.Count; i++)
        {
            if (string.Equals(selection[i]?.Trim(), group, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
