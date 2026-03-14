using Motus.Cli.Services.Reporters;

namespace Motus.Cli.Tests.Services.Reporters;

[TestClass]
public class ReporterFactoryTests
{
    [TestMethod]
    public void Console_ReturnsConsoleReporter()
    {
        var reporter = ReporterFactory.Create(["console"]);
        Assert.IsInstanceOfType<ConsoleReporter>(reporter);
    }

    [TestMethod]
    public void JUnit_ReturnsJUnitReporter()
    {
        var reporter = ReporterFactory.Create(["junit:results.xml"]);
        Assert.IsInstanceOfType<JUnitReporter>(reporter);
    }

    [TestMethod]
    public void Html_ReturnsHtmlReporter()
    {
        var reporter = ReporterFactory.Create(["html:report.html"]);
        Assert.IsInstanceOfType<HtmlReporter>(reporter);
    }

    [TestMethod]
    public void Trx_ReturnsTrxReporter()
    {
        var reporter = ReporterFactory.Create(["trx:results.trx"]);
        Assert.IsInstanceOfType<TrxReporter>(reporter);
    }

    [TestMethod]
    public void MultipleSpecs_ReturnsCompositeReporter()
    {
        var reporter = ReporterFactory.Create(["console", "junit:results.xml"]);
        Assert.IsInstanceOfType<CompositeReporter>(reporter);
    }

    [TestMethod]
    public void UnknownFormat_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            ReporterFactory.Create(["unknown"]));
    }

    [TestMethod]
    public void UnknownFormatWithPath_ThrowsArgumentException()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            ReporterFactory.Create(["csv:output.csv"]));
    }
}
