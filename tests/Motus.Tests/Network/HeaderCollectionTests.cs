namespace Motus.Tests.Network;

[TestClass]
public class HeaderCollectionTests
{
    [TestMethod]
    public void Indexer_ReturnsFirstValue()
    {
        var headers = new HeaderCollection(new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["Accept"] = "text/html"
        });

        Assert.AreEqual("application/json", headers["Content-Type"]);
        Assert.AreEqual("text/html", headers["Accept"]);
    }

    [TestMethod]
    public void Indexer_IsCaseInsensitive()
    {
        var headers = new HeaderCollection(new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json"
        });

        Assert.AreEqual("application/json", headers["content-type"]);
        Assert.AreEqual("application/json", headers["CONTENT-TYPE"]);
    }

    [TestMethod]
    public void Indexer_ThrowsForMissingHeader()
    {
        var headers = new HeaderCollection(new Dictionary<string, string>());
        Assert.ThrowsException<KeyNotFoundException>(() => headers["Missing"]);
    }

    [TestMethod]
    public void GetAll_ReturnsAllValues()
    {
        var entries = new[]
        {
            new FetchHeaderEntry("Set-Cookie", "a=1"),
            new FetchHeaderEntry("Set-Cookie", "b=2"),
            new FetchHeaderEntry("Content-Type", "text/html")
        };

        var headers = new HeaderCollection(entries);
        var cookies = headers.GetAll("Set-Cookie");

        Assert.AreEqual(2, cookies.Count);
        Assert.AreEqual("a=1", cookies[0]);
        Assert.AreEqual("b=2", cookies[1]);
    }

    [TestMethod]
    public void GetAll_ReturnsEmptyForMissing()
    {
        var headers = new HeaderCollection(new Dictionary<string, string>());
        var result = headers.GetAll("Missing");
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void Contains_ReturnsTrueForPresent()
    {
        var headers = new HeaderCollection(new Dictionary<string, string>
        {
            ["X-Custom"] = "value"
        });

        Assert.IsTrue(headers.Contains("X-Custom"));
        Assert.IsTrue(headers.Contains("x-custom"));
        Assert.IsFalse(headers.Contains("Missing"));
    }

    [TestMethod]
    public void Enumeration_ReturnsAllHeaders()
    {
        var headers = new HeaderCollection(new Dictionary<string, string>
        {
            ["A"] = "1",
            ["B"] = "2"
        });

        var list = headers.ToList();
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public void NullDictionary_CreatesEmptyCollection()
    {
        var headers = new HeaderCollection((Dictionary<string, string>?)null);
        Assert.IsFalse(headers.Contains("Anything"));
        Assert.AreEqual(0, headers.ToList().Count);
    }

    [TestMethod]
    public void ToFetchHeaders_ConvertsCorrectly()
    {
        var dict = new Dictionary<string, string>
        {
            ["Content-Type"] = "text/html",
            ["Accept"] = "*/*"
        };

        var result = HeaderCollection.ToFetchHeaders(dict);
        Assert.AreEqual(2, result.Length);
    }

    [TestMethod]
    public void ToFetchHeaders_NullReturnsEmpty()
    {
        var result = HeaderCollection.ToFetchHeaders(null);
        Assert.AreEqual(0, result.Length);
    }
}
