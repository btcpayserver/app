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
    public async Task Test1()
    {
        await using var factory = new BTCPayAppTestServer(_testOutputHelper);
        var page = await (await factory.InitializeAsync()).NewPageAsync();
        await page.GotoAsync(factory.ServerAddress);
        factory.Eventually(() =>
        {
            Assert.EndsWith(Routes.Home, page.Url);
        });

        Thread.Sleep(3000);
        var carousel = await page.QuerySelectorAsync("#OnboardingCarousel");
        await (await carousel.QuerySelectorAsync("[aria-label='3']")).ClickAsync();
        Thread.Sleep(3000);
        await (await page.QuerySelectorAsync("#PairButton")).ClickAsync();
        factory.Eventually(() =>
        {
            Assert.EndsWith(Routes.Pair, page.Url);
        });

    }
}
