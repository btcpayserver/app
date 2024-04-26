using System.Text.Json;
using BTCPayApp.CommonServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.UI.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayApp.Core;

public static class StartupExtensions
{
    public static IServiceCollection ConfigureBTCPayAppCore(this IServiceCollection serviceCollection)
    {
        
        serviceCollection.AddDbContextFactory<AppDbContext>((provider, options) =>
        {
            var dir = provider.GetRequiredService<IDataDirectoryProvider>().GetAppDataDirectory().ConfigureAwait(false).GetAwaiter().GetResult();
            options.UseSqlite($"Data Source={dir}/app.db");
        });
        serviceCollection.AddHostedService<AppDatabaseMigrator>();
        serviceCollection.AddHttpClient();
        serviceCollection.AddSingleton<BTCPayConnection>();
        serviceCollection.AddSingleton<Network>(Network.RegTest);
        serviceCollection.AddSingleton<LightningNodeService>();
        serviceCollection.AddSingleton<IBTCPayAppHubClient,BTCPayAppServerClient>();
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<BTCPayConnection>());
        serviceCollection.AddSingleton<IHostedService>(provider => provider.GetRequiredService<LightningNodeService>());
        
        serviceCollection.AddSingleton<BTCPayAppClient>();
        serviceCollection.AddSingleton<AuthenticationStateProvider, AuthStateProvider>();
        serviceCollection.AddSingleton(sp => (IAccountManager)sp.GetRequiredService<AuthenticationStateProvider>());
        serviceCollection.AddSingleton<IConfigProvider, DatabaseConfigProvider>();

        return serviceCollection;
    }
}

public class DatabaseConfigProvider: IConfigProvider
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public DatabaseConfigProvider(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<T?> Get<T>(string key)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        var config = await dbContext.Settings.FindAsync(key);
        if (typeof(T) == typeof(byte[]))
            return (T?) (config?.Value as object);
        return config is null ? default : JsonSerializer.Deserialize<T>(config.Value);
    }

    public async Task Set<T>(string key, T? value)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        if (value is null)
        {
            try
            {

                dbContext.Settings.Remove(new Setting() {Key = key});
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException e)
            {
            }
            return;
        }

        var newValue = JsonSerializer.SerializeToUtf8Bytes(value);
        await dbContext.Upsert(new Setting() {Key = key, Value = newValue}).RunAsync();

    }
}

public class AppDatabaseMigrator: IHostedService
{
    private readonly ILogger<AppDatabaseMigrator> _logger;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public AppDatabaseMigrator(ILogger<AppDatabaseMigrator> logger, IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _logger = logger;
        _dbContextFactory = dbContextFactory;
    }
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pendingMigrationsAsync = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken: cancellationToken)).ToArray();
        if (pendingMigrationsAsync.Any())
        {
            _logger.LogInformation($"Applying {pendingMigrationsAsync.Length} migrations");
            await dbContext.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Migrations applied: " + string.Join(", ", pendingMigrationsAsync));
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken) { }
}
