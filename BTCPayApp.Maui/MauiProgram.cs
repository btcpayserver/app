using BTCPayApp.Core.Data;
using BTCPayApp.Core.Extensions;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Maui.Services;
using BTCPayApp.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Fingerprint;
using Serilog;

namespace BTCPayApp.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.ConfigureBTCPayAppMaui();
        builder.Services.ConfigureBTCPayAppCore();

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
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Services.AddDangerousSSLSettingsForDev();
        builder.Logging.AddDebug();
#endif
        // Add Serilog to the logging pipeline
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog();

        return builder.Build();
    }
}
