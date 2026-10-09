using Xunit;

namespace Motus.Testing.xUnit;

/// <summary>
/// Base for an xUnit collection whose test classes share one <see cref="SharedBrowserFixture"/>.
/// xUnit reads collection definitions only from the test assembly, so this one does nothing on its
/// own; declare one in the test project that derives from it:
/// <code>
/// [CollectionDefinition("Checkout")]
/// public class CheckoutCollection : MotusCollection { }
/// </code>
/// Classes marked <c>[Collection("Checkout")]</c> then take their context from that browser with
/// <c>IClassFixture&lt;BrowserContextFixture&lt;SharedBrowserFixture&gt;&gt;</c>.
/// </summary>
/// <remarks>
/// A class marked <c>[Collection(nameof(MotusCollection))]</c> without such a definition joins an
/// ordinary collection of that name: its classes run one after another, and a
/// <see cref="BrowserContextFixture"/> in them uses the browser shared by the whole test process.
/// </remarks>
[CollectionDefinition(nameof(MotusCollection))]
public class MotusCollection : ICollectionFixture<SharedBrowserFixture>
{
}
