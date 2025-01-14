using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.App.Data;

public class AppPluginMigrationRunner(AppPluginDbContextFactory dbContextFactory, ISettingsRepository settingsRepository) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings =
            await settingsRepository.GetSettingAsync<AppPluginDataMigrationHistory>() ??
            new AppPluginDataMigrationHistory();

        await using var ctx = dbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken);

        if (!settings.InitialSetup)
        {
            settings.InitialSetup = true;
            await settingsRepository.UpdateSetting(settings);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    private class AppPluginDataMigrationHistory
    {
        public bool InitialSetup { get; set; }
    }
}
