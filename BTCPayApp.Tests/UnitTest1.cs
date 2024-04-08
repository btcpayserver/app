using BTCPayApp.UI;
using BTCPayApp.UI.Pages;
using Xunit.Abstractions;

namespace BTCPayApp.Tests;

public class UnitTest1
{
    private readonly ITestOutputHelper _testOutputHelper;

    public UnitTest1(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    [Fact]
    public async Task TestHomePage()
    {
        await using var factory = new BTCPayAppTestServer(_testOutputHelper);
        var page = await (await factory.InitializeAsync()).NewPageAsync();
        await page.GotoAsync(factory.ServerAddress);
        Assert.EndsWith(Routes.Index, page.Url);

        var carousel = page.Locator("#OnboardingCarousel");
        await carousel.Locator("[aria-label='3']").ClickAsync();
        Assert.True(await carousel.GetByTestId("StandaloneButton").IsDisabledAsync());

        await carousel.GetByTestId("PairButton").ClickAsync();
        Assert.EndsWith(Routes.Pair, page.Url);
    }
}
