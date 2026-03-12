using Motus.Abstractions;
using Motus.Assertions;

namespace Motus.Tests.Assertions;

[TestClass]
public class ResponseAssertionsTests
{
    private sealed class StubResponse : IResponse
    {
        public string Url { get; init; } = "https://example.com/api";
        public int Status { get; init; } = 200;
        public string StatusText { get; init; } = "OK";
        public IHeaderCollection Headers => throw new NotImplementedException();
        public bool Ok => Status >= 200 && Status <= 299;
        public IRequest Request => throw new NotImplementedException();
        public IFrame Frame => throw new NotImplementedException();
        public Task<byte[]> BodyAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> TextAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<T> JsonAsync<T>(CancellationToken ct = default) => throw new NotImplementedException();
    }

    // --- ToBeOkAsync ---

    [TestMethod]
    public async Task ToBeOkAsync_Passes_WhenStatusIs200()
    {
        var response = new StubResponse { Status = 200 };
        await Expect.That(response).ToBeOkAsync();
    }

    [TestMethod]
    public async Task ToBeOkAsync_Passes_WhenStatusIs201()
    {
        var response = new StubResponse { Status = 201 };
        await Expect.That(response).ToBeOkAsync();
    }

    [TestMethod]
    public void ToBeOkAsync_Throws_WhenStatusIs404()
    {
        var response = new StubResponse { Status = 404 };
        var ex = Assert.ThrowsException<MotusAssertionException>(
            () => Expect.That(response).ToBeOkAsync().GetAwaiter().GetResult());
        Assert.AreEqual("404", ex.Actual);
        Assert.IsTrue(ex.Message.Contains("ToBeOk"));
    }

    [TestMethod]
    public void ToBeOkAsync_Throws_WhenStatusIs500()
    {
        var response = new StubResponse { Status = 500 };
        Assert.ThrowsException<MotusAssertionException>(
            () => Expect.That(response).ToBeOkAsync().GetAwaiter().GetResult());
    }

    // --- ToBeOkAsync + Not ---

    [TestMethod]
    public async Task Not_ToBeOkAsync_Passes_WhenStatusIs404()
    {
        var response = new StubResponse { Status = 404 };
        await Expect.That(response).Not.ToBeOkAsync();
    }

    [TestMethod]
    public void Not_ToBeOkAsync_Throws_WhenStatusIs200()
    {
        var response = new StubResponse { Status = 200 };
        var ex = Assert.ThrowsException<MotusAssertionException>(
            () => Expect.That(response).Not.ToBeOkAsync().GetAwaiter().GetResult());
        Assert.AreEqual("200", ex.Actual);
        Assert.IsTrue(ex.Message.Contains("NOT"));
    }

    // --- ToHaveStatusAsync ---

    [TestMethod]
    public async Task ToHaveStatusAsync_Passes_WhenMatch()
    {
        var response = new StubResponse { Status = 201 };
        await Expect.That(response).ToHaveStatusAsync(201);
    }

    [TestMethod]
    public void ToHaveStatusAsync_Throws_WhenNoMatch()
    {
        var response = new StubResponse { Status = 404 };
        var ex = Assert.ThrowsException<MotusAssertionException>(
            () => Expect.That(response).ToHaveStatusAsync(200).GetAwaiter().GetResult());
        Assert.AreEqual("404", ex.Actual);
        Assert.IsTrue(ex.Message.Contains("ToHaveStatus"));
    }

    // --- ToHaveStatusAsync + Not ---

    [TestMethod]
    public async Task Not_ToHaveStatusAsync_Passes_WhenNoMatch()
    {
        var response = new StubResponse { Status = 404 };
        await Expect.That(response).Not.ToHaveStatusAsync(200);
    }

    [TestMethod]
    public void Not_ToHaveStatusAsync_Throws_WhenMatch()
    {
        var response = new StubResponse { Status = 200 };
        var ex = Assert.ThrowsException<MotusAssertionException>(
            () => Expect.That(response).Not.ToHaveStatusAsync(200).GetAwaiter().GetResult());
        Assert.IsTrue(ex.Message.Contains("NOT"));
    }

    // --- Not flips back ---

    [TestMethod]
    public async Task Not_Not_Returns_ToOriginal()
    {
        var response = new StubResponse { Status = 200 };
        await Expect.That(response).Not.Not.ToBeOkAsync();
    }
}
