using System.Reflection;
using Motus.Cli.Services;

namespace Motus.Cli.Tests.Services;

[TestClass]
public class ShardSelectorTests
{
    // A real method to anchor each DiscoveredTest; ShardSelector keys on FullName, so the
    // method itself is irrelevant beyond giving the record a valid Type/MethodInfo.
    public void Anchor() { }

    private List<DiscoveredTest> MakeTests(int count)
    {
        var method = typeof(ShardSelectorTests).GetMethod(nameof(Anchor), BindingFlags.Public | BindingFlags.Instance)!;
        var tests = new List<DiscoveredTest>();
        for (var i = 0; i < count; i++)
            tests.Add(new DiscoveredTest(typeof(ShardSelectorTests), method, $"Ns.Suite.Test{i:D2}", false));
        return tests;
    }

    [TestMethod]
    public void Select_ShardsAreDisjointAndCoverFullSet()
    {
        var all = MakeTests(10);
        const int total = 3;

        var union = new List<string>();
        for (var shard = 1; shard <= total; shard++)
        {
            var subset = ShardSelector.Select(all, shard, total);
            union.AddRange(subset.Select(t => t.FullName));
        }

        // No test appears twice, and every test appears exactly once.
        CollectionAssert.AreEquivalent(
            all.Select(t => t.FullName).ToList(),
            union,
            "The union of all shards must equal the full discovered set with no duplicates.");
    }

    [TestMethod]
    public void Select_IsDeterministicAcrossCalls()
    {
        var all = MakeTests(17);

        var first = ShardSelector.Select(all, 2, 4).Select(t => t.FullName).ToList();
        // Re-shuffle the input order; the stable sort must produce the identical partition.
        var reordered = all.AsEnumerable().Reverse().ToList();
        var second = ShardSelector.Select(reordered, 2, 4).Select(t => t.FullName).ToList();

        CollectionAssert.AreEqual(first, second,
            "The partition must be identical regardless of input enumeration order.");
    }

    [TestMethod]
    public void Select_SingleShardReturnsEverything()
    {
        var all = MakeTests(5);
        var subset = ShardSelector.Select(all, 1, 1);
        Assert.AreEqual(5, subset.Count);
    }

    [TestMethod]
    public void TryParse_ValidSpec_Succeeds()
    {
        Assert.IsTrue(ShardSelector.TryParse("1/3", out var index, out var total, out var error));
        Assert.AreEqual(1, index);
        Assert.AreEqual(3, total);
        Assert.IsNull(error);
    }

    [TestMethod]
    [DataRow("0/3")]
    [DataRow("4/3")]
    [DataRow("2/0")]
    [DataRow("x/y")]
    [DataRow("3")]
    [DataRow("1/2/3")]
    public void TryParse_InvalidSpec_Fails(string spec)
    {
        Assert.IsFalse(ShardSelector.TryParse(spec, out _, out _, out var error));
        Assert.IsNotNull(error);
    }
}
