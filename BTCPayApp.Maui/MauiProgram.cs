using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Maui.Services;
using BTCPayApp.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Fingerprint;

namespace BTCPayApp.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.ConfigureBTCPayAppCore();
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.AddLogging(loggingBuilder => loggingBuilder
            .AddConsole()
        );
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<IDataDirectoryProvider, MauiDataDirectoryProvider>();
        builder.Services.AddSingleton<ISecureConfigProvider, MauiEssentialsSecureConfigProvider>();
        builder.Services.AddSingleton<ISystemThemeProvider, MauiSystemThemeProvider>();
        builder.Services.AddSingleton(CrossFingerprint.Current);
        builder.Services.AddSingleton<HostedServiceInitializer>();
        builder.Services.AddSingleton<IMauiInitializeService, HostedServiceInitializer>();

        builder.ConfigureLifecycleEvents(events =>
        {
            // https://learn.microsoft.com/de-de/dotnet/maui/fundamentals/app-lifecycle#platform-lifecycle-events
#if ANDROID
            events.AddAndroid(android => android
                .OnStart((activity) => LogEvent(nameof(AndroidLifecycle.OnStart)))
                .OnCreate((activity, bundle) =>
                {
                    CrossFingerprint.SetCurrentActivityResolver(() => activity);

                    LogEvent(nameof(AndroidLifecycle.OnCreate));
                })
                .OnStop((activity) => { LogEvent(nameof(AndroidLifecycle.OnStop)); }).OnDestroy(activity =>
                {
                    IPlatformApplication.Current?.Services.GetRequiredService<HostedServiceInitializer>().Dispose();
                }));


#endif
#if IOS
                events.AddiOS(ios => ios
                    .OnActivated((app) => LogEvent(nameof(iOSLifecycle.OnActivated)))
                    .OnResignActivation((app) => LogEvent(nameof(iOSLifecycle.OnResignActivation)))
                    .DidEnterBackground((app) => LogEvent(nameof(iOSLifecycle.DidEnterBackground)))
                    .WillTerminate((app) =>{
 LogEvent(nameof(iOSLifecycle.WillTerminate));

                    IPlatformApplication.Current.Services.GetRequiredService<HostedServiceInitializer>().Dispose();

}
));
#endif
            static bool LogEvent(string eventName, string? type = null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Lifecycle event: {eventName}{(type == null ? string.Empty : $" ({type})")}");
                return true;
            }
        });

        #if DEBUG
        builder.Services.AddDangerousSSLSettingsForDev();
        #endif
        return builder.Build();
    }
}
