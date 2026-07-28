using Motus.Abstractions;

namespace Motus.Assertions;

/// <summary>
/// The entry point to the assertion library: wraps a locator, page, or response in the assertions
/// that apply to it.
/// </summary>
/// <remarks>
/// <para>
/// Assertions read as a sentence, and the ones reached through a locator or a page re-evaluate
/// their condition until it holds or the timeout elapses, rather than testing once:
/// </para>
/// <code>
/// await Expect.That(page.GetByRole("alert")).ToBeVisibleAsync();
/// await Expect.That(page).ToHaveTitleAsync("Checkout");
/// await Expect.That(response).ToBeOkAsync();
/// </code>
/// <para>
/// Re-evaluating covers a value the page has not settled on yet, so an assertion after a
/// navigation or an interaction is safe. It does not cover an element that is not there: a
/// locator assertion needs its element present already and fails at once when the locator matches
/// nothing, so wait for one that has still to render with <c>ToBeAttachedAsync</c> first. Insert
/// <c>Not</c> to invert an assertion: <c>Expect.That(locator).Not.ToBeVisibleAsync()</c>.
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
