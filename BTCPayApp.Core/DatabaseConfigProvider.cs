using System.Collections.Concurrent;
using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BTCPayApp.Core;



public class VSSMapperInterceptor : SaveChangesInterceptor
{

    public VSSMapperInterceptor(BTCPayConnectionManager btcPayConnectionManager, ILogger<VSSMapperInterceptor> logger)
    {
    }
    
    private ConcurrentDictionary<EventId, object> PendingEvents = new ConcurrentDictionary<EventId, object>();
    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result,
        CancellationToken cancellationToken = new CancellationToken())
    {
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = new CancellationToken())
    {
        foreach (var entry in eventData.Context.ChangeTracker.Entries())
        {
            if (entry.Entity is LightningPayment lightningPayment)
            {
            }
            if (entry.Entity is Channel channel)
            {
                
            }
            if (entry.Entity is Setting setting)
            {
                
            }
        }
        
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData,
        CancellationToken cancellationToken = new CancellationToken())
    {
        PendingEvents.Remove(eventData.EventId, out _);
        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData,
        CancellationToken cancellationToken = new CancellationToken())
    {
        PendingEvents.Remove(eventData.EventId, out _);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }
    
    
}

public static class EFExtensions
{

    public static async Task<int> CrappyUpsert<T>(this DbContext ctx, T item, CancellationToken cancellationToken)
    {
        ctx.Attach(item);
        ctx.Entry(item).State = EntityState.Modified;
        try
        {
          return   await ctx.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            ctx.Entry(item).State = EntityState.Added;
            return await ctx.SaveChangesAsync(cancellationToken);
        }
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
                dbContext.Settings.Remove(new Setting {Key = key});
                await dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
            }
            return;
        }

        var newValue = typeof(T) == typeof(byte[])? value as byte[]:JsonSerializer.SerializeToUtf8Bytes(value);
        var setting = new Setting {Key = key, Value = newValue};
        await dbContext.CrappyUpsert(setting, CancellationToken.None);

    }

    public async Task<IEnumerable<string>> List(string prefix)
    {
        await using var dbContext = await _dbContextFactory.CreateDbContextAsync();
        return await dbContext.Settings.Where(s => s.Key.StartsWith(prefix)).Select(s => s.Key).ToListAsync();
    }
}
