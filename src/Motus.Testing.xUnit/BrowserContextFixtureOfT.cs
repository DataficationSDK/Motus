namespace Motus.Testing.xUnit;

/// <summary>
/// xUnit class fixture that creates an isolated browser context and page per test class from a
/// <see cref="SharedBrowserFixture"/> the test's collection provides, rather than from the browser
/// shared by the whole test process.
/// </summary>
/// <typeparam name="TBrowser">
/// The collection fixture to take the browser from: <see cref="SharedBrowserFixture"/>, or a
/// subclass of it that overrides <c>LaunchOptions</c>. xUnit matches the fixture by its exact type,
/// so this must name the same type as the collection's <c>ICollectionFixture&lt;T&gt;</c>.
/// </typeparam>
/// <remarks>
/// xUnit reads collection definitions only from the test assembly, so declare the collection in
/// the test project. Deriving it from <see cref="MotusCollection"/> provides the
/// <see cref="SharedBrowserFixture"/>:
/// <code>
/// [CollectionDefinition("Checkout")]
/// public class CheckoutCollection : MotusCollection { }
///
/// [Collection("Checkout")]
/// public class CartTests : IClassFixture&lt;BrowserContextFixture&lt;SharedBrowserFixture&gt;&gt;
/// {
///     public CartTests(BrowserContextFixture&lt;SharedBrowserFixture&gt; fixture) { ... }
/// }
/// </code>
/// </remarks>
public class BrowserContextFixture<TBrowser> : BrowserContextFixture
    where TBrowser : SharedBrowserFixture
{
    /// <summary>
    /// Creates a fixture whose context comes from the given shared browser.
    /// </summary>
    public BrowserContextFixture(TBrowser browser)
        : base(browser)
    {
        SharedBrowser = browser;
    }

    /// <summary>
    /// The collection fixture that provides this fixture's browser.
    /// </summary>
    public TBrowser SharedBrowser { get; }
}
