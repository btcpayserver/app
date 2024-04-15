using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using BTCPayApp.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BTCPayApp.Core.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Setting> Settings { get; set; }
//     public DbSet<LightningChannel> LightningChannels { get; set; }
//     public DbSet<OnchainCoin> OnchainCoins { get; set; }
//     public DbSet<OnchainScript> OnchainScripts { get; set; }
//     public List<OnChainTransaction> OnChainTransactions { get; set; }
//     public List<LightningTransaction> LightningTransactions { get; set; }
// }
}

public class DatabaseConfigProvider : IConfigProvider
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public DatabaseConfigProvider(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<T?> Get<T>(string key)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var setting = await db.Settings.FindAsync(key);
        return setting == null ? default : JsonSerializer.Deserialize<T>(setting.Value);
    }

    public async Task Set<T>(string key, T? value)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        if (value is null)
        {
            db.Settings.Remove(new Setting {Key = key});
            await db.SaveChangesAsync();
            return;
        }

        var newValue = JsonSerializer.SerializeToUtf8Bytes(value);
        await db.Settings.Upsert(new Setting()
        {
            Key = key,
            Value = newValue
        }).RunAsync();
    }
}

public class Setting
{
    [Key]
    public string Key { get; set; }
    public byte[] Value { get; set; }
}

public class DesignTimeAppContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=fake.db");

        return new AppDbContext(optionsBuilder.Options);
    }
}