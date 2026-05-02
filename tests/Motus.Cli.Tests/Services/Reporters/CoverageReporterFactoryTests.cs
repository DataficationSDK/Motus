using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class CoverageReporterFactoryTests
{
    [TestMethod]
    public void Empty_DefaultsToConsole()
    {
        var reporters = CoverageReporterFactory.Create(Array.Empty<string>());
        Assert.AreEqual(1, reporters.Count);
        Assert.IsInstanceOfType<CoverageConsoleReporter>(reporters[0]);
    }

    [TestMethod]
    public void Null_DefaultsToConsole()
    {
        var reporters = CoverageReporterFactory.Create(null);
        Assert.AreEqual(1, reporters.Count);
        Assert.IsInstanceOfType<CoverageConsoleReporter>(reporters[0]);
    }

    [TestMethod]
    public void Console_ReturnsConsoleReporter()
    {
        var reporters = CoverageReporterFactory.Create(new[] { "console" });
        Assert.IsInstanceOfType<CoverageConsoleReporter>(reporters[0]);
    }

    [TestMethod]
    public void Html_ReturnsHtmlReporter()
    {
        var reporters = CoverageReporterFactory.Create(new[] { "html:./out" });
        Assert.IsInstanceOfType<CoverageHtmlReporter>(reporters[0]);
    }

    [TestMethod]
    public void Cobertura_ReturnsCoberturaReporter()
    {
        var reporters = CoverageReporterFactory.Create(new[] { "cobertura:./coverage.xml" });
        Assert.IsInstanceOfType<CoberturaReporter>(reporters[0]);
    }

    [TestMethod]
    public void Multiple_ReturnsAll()
    {
        var reporters = CoverageReporterFactory.Create(new[] { "console", "html:./out", "cobertura:./c.xml" });
        Assert.AreEqual(3, reporters.Count);
    }

    [TestMethod]
    public void UnknownFormat_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "unknown" }));
    }

    [TestMethod]
    public void UnknownFormatWithPath_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "lcov:./out" }));
    }

    [TestMethod]
    public void HtmlWithoutPath_ThrowsWithGuidance()
    {
        var ex = Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "html" }));

        StringAssert.Contains(ex.Message, "html:<dir>",
            "Error must show the user the correct syntax, not just say it's wrong.");
    }

    [TestMethod]
    public void CoberturaWithoutPath_ThrowsWithGuidance()
    {
        var ex = Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "cobertura" }));

        StringAssert.Contains(ex.Message, "cobertura:<path>");
    }

    [TestMethod]
    public void HtmlWithEmptyPath_Throws()
    {
        // "--coverage html:" — colon is there, target is empty.
        var ex = Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "html:" }));

        StringAssert.Contains(ex.Message, "empty target");
    }

    [TestMethod]
    public void ConsoleWithPath_Throws()
    {
        // "--coverage console:foo" — console does not take a target.
        var ex = Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "console:foo" }));

        StringAssert.Contains(ex.Message, "console");
        StringAssert.Contains(ex.Message, "does not take a target");
    }

    [TestMethod]
    public void UnknownFormat_ListsSupportedFormats()
    {
        var ex = Assert.ThrowsException<ArgumentException>(() =>
            CoverageReporterFactory.Create(new[] { "xml" }));

        StringAssert.Contains(ex.Message, "console");
        StringAssert.Contains(ex.Message, "html");
        StringAssert.Contains(ex.Message, "cobertura");
    }
}
