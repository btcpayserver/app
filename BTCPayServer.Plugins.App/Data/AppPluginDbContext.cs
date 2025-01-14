using BTCPayServer.Plugins.App.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.App.Data;

public class AppPluginDbContext(DbContextOptions<AppPluginDbContext> options) : DbContext(options)
{
    public DbSet<AppStorageItemData> AppStorageItems { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("BTCPayServer.Plugins.App");

        AppStorageItemData.OnModelCreating(modelBuilder);
    }
}
