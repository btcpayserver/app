using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Laraue.EfCoreTriggers.PostgreSql.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Plugins.App.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppPluginDbContext>
{
    public AppPluginDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AppPluginDbContext>();
        builder.UseNpgsql("User ID=postgres;Host=127.0.0.1;Port=39372;Database=designtimebtcpay");
        builder.UsePostgreSqlTriggers();
        return new AppPluginDbContext(builder.Options);
    }
}

public class AppPluginDbContextFactory(IOptions<DatabaseOptions> options) : BaseDbContextFactory<AppPluginDbContext>(options, "BTCPayServer.Plugins.App")
{
    public override AppPluginDbContext CreateContext(Action<NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<AppPluginDbContext>();
        ConfigureBuilder(builder);
        builder.UsePostgreSqlTriggers();
        return new AppPluginDbContext(builder.Options);
    }
}
