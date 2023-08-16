using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Maui.Services;
using BTCPayApp.UI;

namespace BTCPayApp.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.ConfigureBTCPayAppCore();
        builder.Services.AddLogging();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
#endif

        builder.Services.AddSingleton<IDataDirectoryProvider, XamarinDataDirectoryProvider>();
        builder.Services.AddSingleton<IConfigProvider, XamarinEssentialsConfigProvider>();
        builder.Services.AddSingleton<ISecureConfigProvider, XamarinEssentialsSecureConfigProvider>();
        return builder.Build();
    }
}
