namespace Motus;

/// <summary>
/// Role names a rule can compare a tree node against.
/// </summary>
/// <remarks>
/// The accessibility tree reports the browser's own name for a role, which is not always the ARIA
/// name. Chromium calls the image role <c>image</c>, both for an <c>&lt;img&gt;</c> element and for
/// an element carrying an explicit <c>role="img"</c>, so a rule that only looks for <c>img</c>
/// matches nothing on a real page. Rules match through this type so the spellings live in one
/// place, and the role a violation reports stays the one the browser gave, which is also what an
/// accessibility snapshot shows for the same node.
/// </remarks>
internal static class AccessibilityRoleNames
{
    private static readonly HashSet<string> ImageRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "img", "image"
    };

    /// <summary>
    /// Whether the role names an image, under either the ARIA spelling or Chromium's.
    /// </summary>
    internal static bool IsImage(string? role) => role is not null && ImageRoles.Contains(role);
}
