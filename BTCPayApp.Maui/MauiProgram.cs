using BTCPayApp.Core.Extensions;
using BTCPayApp.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Fingerprint;
using BTCPayApp.Core.Contracts;




#if ANDROID
using Android.Content;
#endif

namespace BTCPayApp.Maui;

public static class MauiProgram
{
    public static IServiceProvider Services { get; private set; }

    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureEssentials()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.ConfigureBTCPayAppMaui();
        builder.Services.ConfigureBTCPayAppCore();
        builder.Services.AddSingleton<INfcService, NfcService>();

        var serviceProvider = builder.Services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<MauiApp>>();

        builder.ConfigureLifecycleEvents(events =>
        {
            // https://learn.microsoft.com/de-de/dotnet/maui/fundamentals/app-lifecycle#platform-lifecycle-events
#if ANDROID
            events.AddAndroid(android => android
                .OnCreate((activity, _) =>
                {
                    logger.LogDebug("Lifecycle event: {Name}", nameof(AndroidLifecycle.OnCreate));
                    CrossFingerprint.SetCurrentActivityResolver(() => activity);
                })
                .OnStart(_ =>
                {
                    logger.LogDebug("Lifecycle event: {Name}", nameof(AndroidLifecycle.OnStart));

                    var context = Android.App.Application.Context;
                    var intent = new Intent(context, typeof(HubConnectionForegroundService));
                    context.StopService(intent); // App is in foreground — stop background service
                })
                .OnStop(_ =>
                {
                    logger.LogDebug("Lifecycle event: {Name}", nameof(AndroidLifecycle.OnStop));

                    var context = Android.App.Application.Context;
                    var intent = new Intent(context, typeof(HubConnectionForegroundService));
                    context.StartForegroundService(intent); // App is in background — start service
                })
                .OnDestroy(activity =>
                {
                    logger.LogDebug("Lifecycle event: {Name}", nameof(AndroidLifecycle.OnDestroy));
                    // This event is called whenever we use File.OpenReadStream().CopyToAsync(fs)
                    // Immersive is being used to prevent the services from being disposed when the above method is used.
                    if (activity.Immersive)
                    {
                        IPlatformApplication.Current?.Services.GetRequiredService<HostedServiceInitializer>().Dispose();
                    }
                    // Stop foreground service
                    var context = Android.App.Application.Context;
                    var intent = new Intent(context, typeof(HubConnectionForegroundService));
                    context.StopService(intent);
                }));
#endif
#if IOS
                events.AddiOS(ios => ios
                    .OnActivated((app) => LogEvent(nameof(iOSLifecycle.OnActivated)))
                    .OnResignActivation((app) => LogEvent(nameof(iOSLifecycle.OnResignActivation)))
                    .DidEnterBackground((app) => LogEvent(nameof(iOSLifecycle.DidEnterBackground)))
                    .WillTerminate((app) => LogEvent(nameof(iOSLifecycle.WillTerminate));

                    IPlatformApplication.Current.Services.GetRequiredService<HostedServiceInitializer>().Dispose();
                }
                ));
#endif
        });
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Services.AddDangerousSSLSettingsForDev();
#endif

        var app = builder.Build();
        Services = app.Services;
        return app;
    }
}
