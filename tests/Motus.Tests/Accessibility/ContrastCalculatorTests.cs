namespace Motus.Tests.Accessibility;

[TestClass]
public class ContrastCalculatorTests
{
    [TestMethod]
    public void RelativeLuminance_Black_ReturnsZero()
    {
        var lum = ContrastCalculator.RelativeLuminance(0, 0, 0);
        Assert.AreEqual(0.0, lum, 0.001);
    }

    [TestMethod]
    public void RelativeLuminance_White_ReturnsOne()
    {
        var lum = ContrastCalculator.RelativeLuminance(255, 255, 255);
        Assert.AreEqual(1.0, lum, 0.001);
    }

    [TestMethod]
    public void ContrastRatio_BlackOnWhite_Returns21()
    {
        var black = ContrastCalculator.RelativeLuminance(0, 0, 0);
        var white = ContrastCalculator.RelativeLuminance(255, 255, 255);
        var ratio = ContrastCalculator.ContrastRatio(white, black);
        Assert.AreEqual(21.0, ratio, 0.01);
    }

    [TestMethod]
    public void ContrastRatio_SameColor_Returns1()
    {
        var lum = ContrastCalculator.RelativeLuminance(128, 128, 128);
        var ratio = ContrastCalculator.ContrastRatio(lum, lum);
        Assert.AreEqual(1.0, ratio, 0.001);
    }

    [TestMethod]
    public void ContrastRatio_OrderDoesNotMatter()
    {
        var l1 = ContrastCalculator.RelativeLuminance(50, 50, 50);
        var l2 = ContrastCalculator.RelativeLuminance(200, 200, 200);
        Assert.AreEqual(
            ContrastCalculator.ContrastRatio(l1, l2),
            ContrastCalculator.ContrastRatio(l2, l1),
            0.001);
    }

    [TestMethod]
    [DataRow("rgb(0, 0, 0)", 0, 0, 0)]
    [DataRow("rgb(255, 255, 255)", 255, 255, 255)]
    [DataRow("rgba(128, 64, 32, 1)", 128, 64, 32)]
    [DataRow("rgb(10,20,30)", 10, 20, 30)]
    public void TryParseColor_Rgb_Succeeds(string color, int er, int eg, int eb)
    {
        Assert.IsTrue(ContrastCalculator.TryParseColor(color, out var r, out var g, out var b));
        Assert.AreEqual(er, r);
        Assert.AreEqual(eg, g);
        Assert.AreEqual(eb, b);
    }

    [TestMethod]
    [DataRow("#000000", 0, 0, 0)]
    [DataRow("#ffffff", 255, 255, 255)]
    [DataRow("#ff8040", 255, 128, 64)]
    [DataRow("#fff", 255, 255, 255)]
    [DataRow("#000", 0, 0, 0)]
    public void TryParseColor_Hex_Succeeds(string color, int er, int eg, int eb)
    {
        Assert.IsTrue(ContrastCalculator.TryParseColor(color, out var r, out var g, out var b));
        Assert.AreEqual(er, r);
        Assert.AreEqual(eg, g);
        Assert.AreEqual(eb, b);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("transparent")]
    [DataRow("red")]
    public void TryParseColor_Invalid_ReturnsFalse(string? color)
    {
        Assert.IsFalse(ContrastCalculator.TryParseColor(color, out _, out _, out _));
    }

    [TestMethod]
    [DataRow("24px", "400", true)]    // >= 24px = large
    [DataRow("25px", "400", true)]
    [DataRow("18.66px", "700", true)] // bold >= 18.66px = large
    [DataRow("19px", "bold", true)]
    [DataRow("16px", "400", false)]   // normal size, normal weight
    [DataRow("16px", "700", false)]   // bold but < 18.66px
    [DataRow("18px", "700", false)]   // bold but < 18.66px
    [DataRow("23px", "400", false)]   // < 24px, not bold
    public void IsLargeText_VariousCombinations(string fontSize, string fontWeight, bool expected)
    {
        Assert.AreEqual(expected, ContrastCalculator.IsLargeText(fontSize, fontWeight));
    }

    [TestMethod]
    public void IsLargeText_NullValues_ReturnsFalse()
    {
        Assert.IsFalse(ContrastCalculator.IsLargeText(null, null));
        Assert.IsFalse(ContrastCalculator.IsLargeText("", ""));
    }

    [TestMethod]
    public void KnownContrastRatio_GrayOnWhite()
    {
        // #767676 on white is the famous 4.54:1 ratio (just passes AA)
        var fg = ContrastCalculator.RelativeLuminance(0x76, 0x76, 0x76);
        var bg = ContrastCalculator.RelativeLuminance(255, 255, 255);
        var ratio = ContrastCalculator.ContrastRatio(fg, bg);
        Assert.IsTrue(ratio >= 4.5, $"Expected >= 4.5 but was {ratio:F2}");
    }
}
