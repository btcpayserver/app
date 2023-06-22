using Comrade.Core;
using Comrade.Core.Contracts;
using Comrade.Maui.Services;
using Comrade.UI;

namespace Comrade.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddComradeUIServices();
        builder.Services.ConfigureComradeCore();
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