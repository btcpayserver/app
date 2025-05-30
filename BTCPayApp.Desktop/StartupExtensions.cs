using BTCPayApp.Core.Contracts;
using BTCPayApp.Desktop.Services;
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
        serviceCollection.AddSingleton<ISecureConfigProvider, DesktopSecureConfigProvider>();
        serviceCollection.AddSingleton<IFingerprint, StubFingerprintProvider>();
        serviceCollection.AddScoped<IEmailService, EmailService>();

        return serviceCollection;
    }
}
