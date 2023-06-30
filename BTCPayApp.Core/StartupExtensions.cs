using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayApp.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<BTCPayAppConfigManager>();
        serviceCollection.AddSingleton<IHostedService, BTCPayAppConfigManager>(provider =>
            provider.GetRequiredService<BTCPayAppConfigManager>());
        return serviceCollection;
    }
}