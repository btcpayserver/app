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
            Assert.EndsWith(Routes.FirstRun, page.Url);
        });
        await (await page.QuerySelectorAsync("#btn-pair")).ClickAsync();
        factory.Eventually(() =>
        {
            Assert.EndsWith(Routes.Pair, page.Url);
        });
        
    }
}