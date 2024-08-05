using BTCPayApp.CommonServer;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.LDK;
using Laraue.EfCoreTriggers.SqlLite.Extensions;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayApp.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppCore(this IServiceCollection serviceCollection)
    {
        serviceCollection.AddDbContextFactory<AppDbContext>((provider, options) =>
        {
            var dir = provider.GetRequiredService<IDataDirectoryProvider>().GetAppDataDirectory().ConfigureAwait(false).GetAwaiter().GetResult();
            options.UseSqlite($"Data Source={dir}/app.db");
            options.UseSqlLiteTriggers();
        }); 
        
        serviceCollection.AddMemoryCache();
        serviceCollection.AddHostedService<AppDatabaseMigrator>();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<BTCPayConnectionManager>();
        serviceCollection.AddSingleton<SyncService>();
        serviceCollection.AddSingleton<LightningNodeManager>();
        serviceCollection.AddSingleton<OnChainWalletManager>();
        serviceCollection.AddSingleton<BTCPayAppServerClient>();
        serviceCollection.AddSingleton<IBTCPayAppHubClient>(provider => provider.GetRequiredService<BTCPayAppServerClient>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BTCPayConnectionManager>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LightningNodeManager>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<OnChainWalletManager>());
        serviceCollection.AddSingleton<AuthStateProvider>();
        serviceCollection.AddSingleton<AuthenticationStateProvider, AuthStateProvider>( provider => provider.GetRequiredService<AuthStateProvider>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<AuthStateProvider>());
        serviceCollection.AddSingleton(sp => (IAccountManager)sp.GetRequiredService<AuthenticationStateProvider>());
        serviceCollection.AddSingleton<IConfigProvider, DatabaseConfigProvider>();
        serviceCollection.AddLDK();
        return serviceCollection;
    }
}