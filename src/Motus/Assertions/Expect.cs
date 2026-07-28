using Motus.Abstractions;

namespace Motus.Assertions;

/// <summary>
/// The entry point to the assertion library: wraps a locator, page, or response in the assertions
/// that apply to it.
/// </summary>
/// <remarks>
/// <para>
/// Assertions read as a sentence, and the ones reached through a locator or a page retry until
/// they hold rather than testing once:
/// </para>
/// <code>
/// await Expect.That(page.GetByRole("alert")).ToBeVisibleAsync();
/// await Expect.That(page).ToHaveTitleAsync("Checkout");
/// await Expect.That(response).ToBeOkAsync();
/// </code>
/// <para>
/// Because they retry, an explicit wait before an assertion is unnecessary. Insert <c>Not</c> to
/// invert one: <c>Expect.That(locator).Not.ToBeVisibleAsync()</c>.
/// </para>
/// </remarks>
public static class Expect
{
    /// <summary>Begins an assertion about the element a locator resolves to.</summary>
    /// <param name="locator">The locator to assert against.</param>
    public static LocatorAssertions That(ILocator locator) => new((Locator)locator);

    /// <summary>Begins an assertion about a page as a whole.</summary>
    /// <param name="page">The page to assert against.</param>
    public static PageAssertions That(IPage page) => new((Page)page);

    /// <summary>Begins an assertion about a response the browser received.</summary>
    /// <param name="response">The response to assert against.</param>
    public static ResponseAssertions That(IResponse response) => new(response);
}
