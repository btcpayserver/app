using BTCPayApp.Core.Extensions;
using BTCPayApp.Maui.Services;
using BTCPayApp.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Fingerprint;
using Serilog;
using Serilog.Extensions.Logging;

namespace BTCPayApp.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var storageService = new MauiDataDirectoryProvider();
        var appdirectory = storageService.GetAppDataDirectory().GetAwaiter().GetResult();
        // Configure Serilog
        string logFilePath = Path.Combine(appdirectory, "logs", "app_log_.txt");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Error() // Set minimum log level
            .WriteTo.Console() // Output to debug console
            .WriteTo.File(
                logFilePath,
                rollingInterval: RollingInterval.Day, // Creates new file daily
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 1024 * 1024, // 1MB limit
                retainedFileCountLimit: 7) // Keep 7 days of logs
            .Enrich.FromLogContext()
            .CreateLogger();

        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        // Clear any existing providers and add Serilog
        builder.Logging
            .ClearProviders() // Optional: removes default providers
            .AddProvider(new SerilogLoggerProvider(Log.Logger, dispose: true));

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.ConfigureBTCPayAppCore();
        builder.Services.ConfigureBTCPayAppMaui();
        //builder.Services.AddLogging(loggingBuilder => loggingBuilder.AddConsole());

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
        //builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Services.AddDangerousSSLSettingsForDev();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
