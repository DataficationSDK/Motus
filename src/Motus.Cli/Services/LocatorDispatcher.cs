using Motus.Abstractions;

namespace Motus.Cli.Services;

/// <summary>
/// Maps a <see cref="ParsedSelector"/> to the matching <see cref="IPage"/> locator
/// factory call so <c>check-selectors</c> can exercise selectors against a live page
/// using the same method the test code uses.
/// </summary>
internal static class LocatorDispatcher
{
    internal static ILocator Dispatch(IPage page, ParsedSelector selector)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(selector);

        var arg = selector.Selector;
        return selector.LocatorMethod switch
        {
            "GetByRole"        => page.GetByRole(NormalizeRole(arg)),
            "GetByText"        => page.GetByText(arg),
            "GetByLabel"       => page.GetByLabel(arg),
            "GetByPlaceholder" => page.GetByPlaceholder(arg),
            "GetByTestId"      => page.GetByTestId(arg),
            "GetByTitle"       => page.GetByTitle(arg),
            "GetByAltText"     => page.GetByAltText(arg),
            _                  => page.Locator(arg),
        };
    }

    // The Roslyn parser captures member-access expressions verbatim, so a
    // call like GetByRole(AriaRole.Button) becomes the selector string
    // "AriaRole.Button". IPage.GetByRole takes a plain ARIA role string
    // ("button"), so strip the enum prefix and lowercase the remainder.
    internal static string NormalizeRole(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return raw;

        var dot = raw.LastIndexOf('.');
        var name = dot >= 0 ? raw[(dot + 1)..] : raw;
        return name.ToLowerInvariant();
    }
}
