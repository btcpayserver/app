using BTCPayApp.CommonServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayApp.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<BTCPayConnection>();
        serviceCollection.AddSingleton<IBTCPayAppServerClient,BTCPayAppServerClient>();
        serviceCollection.AddSingleton<BTCPayAppConfigManager>();
        serviceCollection.AddSingleton<LightningNodeManager>();
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<BTCPayAppConfigManager>());
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<BTCPayConnection>());
        serviceCollection.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<LightningNodeManager>());
        return serviceCollection;
    }
}