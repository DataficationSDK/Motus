namespace Motus.Tests.Coverage;

[TestClass]
public class VlqTests
{
    [TestMethod]
    public void Decode_SingleZero_ReturnsZero()
    {
        var result = Vlq.Decode("A");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(0, result[0]);
    }

    [TestMethod]
    public void Decode_PositiveOne_ReturnsOne()
    {
        var result = Vlq.Decode("C");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(1, result[0]);
    }

    [TestMethod]
    public void Decode_NegativeOne_ReturnsMinusOne()
    {
        var result = Vlq.Decode("D");
        Assert.AreEqual(-1, result[0]);
    }

    [TestMethod]
    public void Decode_TwoDigit_ReturnsSixteen()
    {
        // 16 = 0b100000; sign-shifted = 0b1000000 → bits split 5+1: chunk0 = 0b00000 (cont), chunk1 = 0b00001
        // Encoded as 'g' (32, with continuation) + 'B' (1).
        var result = Vlq.Decode("gB");
        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(16, result[0]);
    }

    [TestMethod]
    public void Decode_FourFieldSegment_ReturnsAllFields()
    {
        // "AAgBC" → [0, 0, 16, 1]
        var result = Vlq.Decode("AAgBC");
        CollectionAssert.AreEqual(new[] { 0, 0, 16, 1 }, result.ToArray());
    }

    [TestMethod]
    public void Decode_AllZeros_ReturnsZeros()
    {
        var result = Vlq.Decode("AAAA");
        CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, result.ToArray());
    }

    [TestMethod]
    [ExpectedException(typeof(FormatException))]
    public void Decode_InvalidChar_Throws()
    {
        Vlq.Decode("!");
    }

    [TestMethod]
    [ExpectedException(typeof(FormatException))]
    public void Decode_TruncatedContinuation_Throws()
    {
        // 'g' has continuation bit set but no following digit.
        Vlq.Decode("g");
    }
}
