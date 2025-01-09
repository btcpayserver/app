using BTCPayApp.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core;

public class AppDatabaseMigrator(ILogger<AppDatabaseMigrator> logger, IDbContextFactory<AppDbContext> dbContextFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var pendingMigrationsAsync = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken: cancellationToken)).ToArray();
        if (pendingMigrationsAsync.Length != 0)
        {
            logger.LogInformation("Applying {Length} migrations", pendingMigrationsAsync.Length);
            await dbContext.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Migrations applied: {Migrations}", string.Join(", ", pendingMigrationsAsync));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
