using BTCPayServer.Lightning;
using BTCPayServer.Plugins.App.Data;
using Laraue.EfCoreTriggers.PostgreSql.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.App.Extensions;

public static class AppExtensions
{
    public static IServiceCollection AddBTCPayApp(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddGrpc();
        serviceCollection.AddSingleton<BTCPayAppState>();
        serviceCollection.AddSingleton<ILightningConnectionStringHandler, BTCPayAppLightningConnectionStringHandler>();
        serviceCollection.AddSingleton<AppPluginDbContextFactory>();
        serviceCollection.AddDbContext<AppPluginDbContext>((provider, o) =>
        {
            var factory = provider.GetRequiredService<AppPluginDbContextFactory>();
            factory.ConfigureBuilder(o);
            o.UsePostgreSqlTriggers();
        });
        serviceCollection.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<BTCPayAppState>());
        serviceCollection.AddHostedService<AppPluginMigrationRunner>();
        return serviceCollection;
    }

    public static void UseBTCPayApp(this IApplicationBuilder builder)
    {
        builder.UseEndpoints(routeBuilder =>
        {
            routeBuilder.MapHub<BTCPayAppHub>("hub/btcpayapp");
        });
    }
}
