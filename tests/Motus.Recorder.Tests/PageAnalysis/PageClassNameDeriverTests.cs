using Motus.Recorder.PageAnalysis;

namespace Motus.Recorder.Tests.PageAnalysis;

[TestClass]
public class PageClassNameDeriverTests
{
    [TestMethod]
    public void Derive_SimpleUrl_ReturnsExpected()
    {
        var result = PageClassNameDeriver.Derive("https://example.com/login");
        Assert.AreEqual("ExampleComLoginPage", result);
    }

    [TestMethod]
    public void Derive_RootUrl_ReturnsHostPage()
    {
        var result = PageClassNameDeriver.Derive("https://example.com");
        Assert.AreEqual("ExampleComPage", result);
    }

    [TestMethod]
    public void Derive_StripsWww()
    {
        var result = PageClassNameDeriver.Derive("https://www.example.com/about");
        Assert.AreEqual("ExampleComAboutPage", result);
    }

    [TestMethod]
    public void Derive_MultiplePathSegments()
    {
        var result = PageClassNameDeriver.Derive("https://app.example.com/settings/profile");
        Assert.AreEqual("AppExampleComSettingsProfilePage", result);
    }

    [TestMethod]
    public void Derive_HandlesSubdomain()
    {
        var result = PageClassNameDeriver.Derive("https://dashboard.myapp.io");
        Assert.AreEqual("DashboardMyappIoPage", result);
    }

    [TestMethod]
    public void Derive_InvalidUrl_ReturnsUnknownPage()
    {
        var result = PageClassNameDeriver.Derive("not-a-url");
        Assert.AreEqual("UnknownPage", result);
    }

    [TestMethod]
    public void Derive_UrlWithQueryString_IgnoresQuery()
    {
        var result = PageClassNameDeriver.Derive("https://example.com/search?q=test");
        Assert.AreEqual("ExampleComSearchPage", result);
    }

    [TestMethod]
    public void Derive_UrlWithPort_IncludesHost()
    {
        var result = PageClassNameDeriver.Derive("http://localhost:3000/dashboard");
        Assert.AreEqual("LocalhostDashboardPage", result);
    }

    [TestMethod]
    public void Derive_UrlWithHyphens_ConvertsToPascalCase()
    {
        var result = PageClassNameDeriver.Derive("https://my-app.example.com/sign-in");
        Assert.AreEqual("MyAppExampleComSignInPage", result);
    }
}
