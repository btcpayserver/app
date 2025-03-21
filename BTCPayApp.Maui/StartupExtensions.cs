﻿using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Maui.Services;
using Plugin.Fingerprint;

namespace BTCPayApp.Maui;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppMaui(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<IDataDirectoryProvider, MauiDataDirectoryProvider>();
        serviceCollection.AddSingleton<ISecureConfigProvider, MauiEssentialsSecureConfigProvider>();
        serviceCollection.AddSingleton<ISystemThemeProvider, MauiSystemThemeProvider>();
        serviceCollection.AddSingleton(CrossFingerprint.Current);
        serviceCollection.AddSingleton<HostedServiceInitializer>();
        serviceCollection.AddSingleton<IMauiInitializeService, HostedServiceInitializer>();

        return serviceCollection;
    }
}
