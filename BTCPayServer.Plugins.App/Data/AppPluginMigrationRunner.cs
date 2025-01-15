using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.App.Data;

public class AppPluginMigrationRunner(
    ILogger<AppPluginMigrationRunner> logger,
    AppPluginDbContextFactory dbContextFactory,
    ISettingsRepository settingsRepository) : IStartupTask
{

    private class AppPluginDataMigrationHistory
    {
        public bool InitialSetup { get; set; }
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var settings =
            await settingsRepository.GetSettingAsync<AppPluginDataMigrationHistory>() ??
            new AppPluginDataMigrationHistory();
        
        await using var ctx = dbContextFactory.CreateContext();
        var migrations = await ctx.Database.GetAppliedMigrationsAsync(cancellationToken: cancellationToken);
        var allMigrations = ctx.Database.GetMigrations();
        var pendingMigrations = await ctx.Database.GetPendingMigrationsAsync(cancellationToken: cancellationToken);
        if (pendingMigrations.Any())
        {
            logger.LogInformation("Applying {Count} migrations", pendingMigrations.Count());
            await ctx.Database.MigrateAsync(cancellationToken: cancellationToken);
        }
        else
        {
            logger.LogInformation("No migrations to apply");
        }
        if (!settings.InitialSetup)
        {
            settings.InitialSetup = true;
            await settingsRepository.UpdateSetting(settings);
        }
    }
}