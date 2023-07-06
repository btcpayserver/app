using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayApp.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<BTCPayConnection>();
        serviceCollection.AddSingleton<BTCPayAppConfigManager>();
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<BTCPayAppConfigManager>());
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<BTCPayConnection>());
        return serviceCollection;
    }
}