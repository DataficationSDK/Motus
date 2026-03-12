using Motus.Abstractions;

namespace Motus.Assertions;

public static class Expect
{
    public static LocatorAssertions That(ILocator locator) => new((Locator)locator);
    public static PageAssertions That(IPage page) => new((Page)page);
    public static ResponseAssertions That(IResponse response) => new(response);
}
