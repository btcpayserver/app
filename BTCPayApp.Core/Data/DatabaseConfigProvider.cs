using System.Text.Json;
using BTCPayApp.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core.Data;

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
        var setting = new Setting()
        {
            Key = key,
            Value = newValue
        };
        await db.Settings
            .Upsert(setting)
            // .WhenMatched((existing, provided) => new Setting
            // {
            //     Version = existing.Version + 1
            // })
            .RunAsync();
        
    }
}