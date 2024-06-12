using System.Text.Json;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core;

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
                dbContext.Settings.Remove(new Setting {Key = key});
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
            }
            return;
        }

        var newValue = typeof(T) == typeof(byte[])? value as byte[]:JsonSerializer.SerializeToUtf8Bytes(value);
        await dbContext.Upsert(new Setting {Key = key, Value = newValue}).RunAsync();

    }

    public async Task<IEnumerable<string>> List(string prefix)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.Settings.Where(s => s.Key.StartsWith(prefix)).Select(s => s.Key).ToListAsync();
    }
}
