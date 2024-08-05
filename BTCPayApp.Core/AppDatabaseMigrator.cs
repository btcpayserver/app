using BTCPayApp.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core;

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