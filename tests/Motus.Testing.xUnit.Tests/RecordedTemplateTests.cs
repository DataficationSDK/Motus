using Motus.Abstractions;
using Motus.Testing.xUnit;

namespace Motus.Testing.xUnit.Tests;

// The class `motus record --framework xunit` emits (see FrameworkTemplate.GetXUnitHeader in
// Motus.Recorder), with only the trait added so the integration run picks it up. It compiles and
// runs here with nothing else declared, as it must in a user's project.

[Trait("TestCategory", "Integration")]
public class RecordedTest : IClassFixture<BrowserContextFixture>
{
    private readonly BrowserContextFixture _fixture;

    public RecordedTest(BrowserContextFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RecordedScenario()
    {
        var page = _fixture.Page;

        await page.GotoAsync("data:text/html,<title>Recorded</title>");
        Assert.Equal("Recorded", await page.TitleAsync());
    }
}
