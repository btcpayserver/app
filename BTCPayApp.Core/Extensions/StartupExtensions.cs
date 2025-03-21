using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Backup;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayApp.Core.Wallet;
using Laraue.EfCoreTriggers.SqlLite.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BTCPayApp.Core.Extensions;

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
        serviceCollection.AddHttpClient();
        serviceCollection.AddHostedService<AppDatabaseMigrator>();
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
        serviceCollection.AddSingleton<AuthenticationStateProvider, AuthStateProvider>(provider => provider.GetRequiredService<AuthStateProvider>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<AuthStateProvider>());
        serviceCollection.AddSingleton(sp => (IAccountManager)sp.GetRequiredService<AuthenticationStateProvider>());
        serviceCollection.AddSingleton<ConfigProvider, DatabaseConfigProvider>();
        serviceCollection.AddLDK();
        serviceCollection.AddSingleton<IAuthorizationHandler, AuthorizationHandler>();
        serviceCollection.AddAuthorizationCore(options => options.AddPolicies());

        // Configure logging
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var dirProvider = serviceProvider.GetRequiredService<IDataDirectoryProvider>();
        var logHelper = new LogHelper(dirProvider);
        var logFilePath = logHelper.GetLogPath().GetAwaiter().GetResult();
        LoggingConfig.ConfigureLogging(logFilePath);

        return serviceCollection;
    }
}
