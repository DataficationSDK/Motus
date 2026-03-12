using Xunit;

namespace Motus.Testing.xUnit;

/// <summary>
/// xUnit collection definition that shares a single browser across all test classes
/// decorated with <c>[Collection(nameof(MotusCollection))]</c>.
/// </summary>
[CollectionDefinition(nameof(MotusCollection))]
public class MotusCollection : ICollectionFixture<SharedBrowserFixture>
{
}
