﻿using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Fingerprint.Abstractions;

namespace BTCPayApp.Desktop;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppDesktop(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddDataProtection(options =>
        {
            options.ApplicationDiscriminator = "BTCPayApp";
        });
        serviceCollection.AddSingleton<IDataDirectoryProvider, DesktopDataDirectoryProvider>();
        // serviceCollection.AddSingleton<IConfigProvider, DesktopConfigProvider>();
        serviceCollection.AddSingleton<ISecureConfigProvider, DesktopSecureConfigProvider>();
        serviceCollection.AddSingleton<IFingerprint, StubFingerprintProvider>();

        return serviceCollection;
    }
}
