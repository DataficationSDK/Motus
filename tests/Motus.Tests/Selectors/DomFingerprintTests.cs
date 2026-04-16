using Motus.Selectors;

namespace Motus.Tests.Selectors;

[TestClass]
public class DomFingerprintTests
{
    [TestMethod]
    public void ComputeHash_SameInputs_ProducesSameHash()
    {
        var attrs = new Dictionary<string, string>
        {
            ["id"] = "submit",
            ["role"] = "button",
        };

        var a = DomFingerprintBuilder.ComputeHash("button", attrs, "Sign in", "div > form > section");
        var b = DomFingerprintBuilder.ComputeHash("button", attrs, "Sign in", "div > form > section");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeHash_AttributeOrderIndependent()
    {
        var ordered = new Dictionary<string, string>
        {
            ["id"] = "submit",
            ["role"] = "button",
            ["data-testid"] = "login-submit",
        };

        var reversed = new Dictionary<string, string>
        {
            ["data-testid"] = "login-submit",
            ["role"] = "button",
            ["id"] = "submit",
        };

        var a = DomFingerprintBuilder.ComputeHash("button", ordered, null, "form");
        var b = DomFingerprintBuilder.ComputeHash("button", reversed, null, "form");

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void ComputeHash_DifferentAncestorPath_ProducesDifferentHash()
    {
        var attrs = new Dictionary<string, string> { ["id"] = "x" };

        var a = DomFingerprintBuilder.ComputeHash("div", attrs, null, "a > b");
        var b = DomFingerprintBuilder.ComputeHash("div", attrs, null, "a > c");

        Assert.AreNotEqual(a, b);
    }

    [TestMethod]
    public void ComputeHash_ReturnsLowerHex()
    {
        var hash = DomFingerprintBuilder.ComputeHash("div", new Dictionary<string, string>(), null, string.Empty);

        Assert.AreEqual(64, hash.Length);
        Assert.AreEqual(hash.ToLowerInvariant(), hash);
        foreach (var c in hash)
            Assert.IsTrue(char.IsDigit(c) || (c >= 'a' && c <= 'f'), $"Non-hex char '{c}' in hash");
    }

    [TestMethod]
    public void ExtractAndTruncateText_StripsTagsAndCollapsesWhitespace()
    {
        var result = DomFingerprintBuilder.ExtractAndTruncateText(
            "<button>  Sign   <span>in</span>  now  </button>");

        Assert.AreEqual("Sign in now", result);
    }

    [TestMethod]
    public void ExtractAndTruncateText_Truncates_At100Chars()
    {
        var longText = new string('a', 500);
        var result = DomFingerprintBuilder.ExtractAndTruncateText($"<div>{longText}</div>");

        Assert.IsNotNull(result);
        Assert.AreEqual(100, result.Length);
    }

    [TestMethod]
    public void ExtractAndTruncateText_EmptyInput_ReturnsNull()
    {
        Assert.IsNull(DomFingerprintBuilder.ExtractAndTruncateText(null));
        Assert.IsNull(DomFingerprintBuilder.ExtractAndTruncateText(string.Empty));
        Assert.IsNull(DomFingerprintBuilder.ExtractAndTruncateText("<div></div>"));
        Assert.IsNull(DomFingerprintBuilder.ExtractAndTruncateText("<div>   </div>"));
    }
}
