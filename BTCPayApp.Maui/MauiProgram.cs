﻿using BTCPayApp.Core;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Maui.Services;
using BTCPayApp.UI;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Plugin.Fingerprint;
using Plugin.Fingerprint.Abstractions;
using Shiny;

namespace BTCPayApp.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>()
            .ConfigureFonts(fonts => { fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular"); });

        builder.UseShiny();
        builder.Services.AddMauiBlazorWebView();
        builder.Services.ConfigureBTCPayAppCore();
        builder.Services.AddBTCPayAppUIServices();
        builder.Services.AddLogging();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        builder.Services.AddSingleton<IDataDirectoryProvider, MauiDataDirectoryProvider>();
        builder.Services.AddSingleton<ISecureConfigProvider, MauiEssentialsSecureConfigProvider>();
        builder.Services.AddSingleton<ISystemThemeProvider, MauiSystemThemeProvider>();
        builder.Services.AddSingleton(typeof(IFingerprint), CrossFingerprint.Current);
        builder.ConfigureLifecycleEvents(events =>
        {
            // https://learn.microsoft.com/de-de/dotnet/maui/fundamentals/app-lifecycle#platform-lifecycle-events
#if ANDROID
            events.AddAndroid(android => android
                .OnStart((activity) => LogEvent(nameof(AndroidLifecycle.OnStart)))
                .OnCreate((activity, bundle) => LogEvent(nameof(AndroidLifecycle.OnCreate)))
                .OnStop((activity) => LogEvent(nameof(AndroidLifecycle.OnStop))));
            
            builder.Services.AddSingleton<AndroidHostedServiceForegreoundService>();

#endif
#if IOS
                events.AddiOS(ios => ios
                    .OnActivated((app) => LogEvent(nameof(iOSLifecycle.OnActivated)))
                    .OnResignActivation((app) => LogEvent(nameof(iOSLifecycle.OnResignActivation)))
                    .DidEnterBackground((app) => LogEvent(nameof(iOSLifecycle.DidEnterBackground)))
                    .WillTerminate((app) => LogEvent(nameof(iOSLifecycle.WillTerminate))));
#endif
            static bool LogEvent(string eventName, string type = null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"Lifecycle event: {eventName}{(type == null ? string.Empty : $" ({type})")}");
                return true;
            }
        });
        return builder.Build();
    }
}

public class StartupService:Shiny.IShinyStartupTask
{
    private readonly IEnumerable<IHostedService> _hostedServices;

    public StartupService(IEnumerable<IHostedService> hostedServices)
    {
        _hostedServices = hostedServices;
    }
    public void Start()
    {
       Task.WhenAll(_hostedServices.Select(hs => hs.StartAsync(CancellationToken.None))).GetAwaiter().GetResult();
    }
}
