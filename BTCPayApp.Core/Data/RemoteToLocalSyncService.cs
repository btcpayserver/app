using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VSSProto;

namespace BTCPayApp.Core.Data;

class TriggerRecord
{
    public string name { get; set; }
    public string sql { get; set; }
}
public class RemoteToLocalSyncService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;

    public RemoteToLocalSyncService(IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager btcPayConnectionManager)
    {
        _dbContextFactory = dbContextFactory;
        _btcPayConnectionManager = btcPayConnectionManager;
    }

    private async Task<KeyValue[]> CreateLocalVersions(AppDbContext dbContext)
    {
        var settings = dbContext.Settings.Where(setting => setting.Backup).Select(setting => new KeyValue()
        {
            Key = setting.EntityKey,
            Version = setting.Version
        });
        var channels = dbContext.LightningChannels.Select(channel => new KeyValue()
        {
            Key = channel.EntityKey,
            Version = channel.Version
        });
        var payments = dbContext.LightningPayments.Select(payment => new KeyValue()
        {
            Key = payment.EntityKey,
            Version = payment.Version
        });
        return await settings.Concat(channels).Concat(payments).ToArrayAsync();
    }

    public async Task Sync()
    {
        var backupApi = await _btcPayConnectionManager.GetVSSAPI();
        await using var db = await _dbContextFactory.CreateDbContextAsync();
        var localVersions = await CreateLocalVersions(db);
        var remoteVersions = await backupApi.ListKeyVersionsAsync(new ListKeyVersionsRequest());
        await db.Database.BeginTransactionAsync();
        try
        {
            
 
            var triggers = await db.Database.SqlQuery<TriggerRecord>($"SELECT name, sql FROM sqlite_master WHERE type = 'trigger'").ToListAsync();
            await db.Database.ExecuteSqlRawAsync(   string.Join("; ", triggers.Select(trigger => $"DROP TRIGGER IF EXISTS {trigger.name}")));
            
            // delete local versions that are not in remote
            // delete local versions which are lower than remote

            var toDelete = localVersions.Where(localVersion =>
                remoteVersions.KeyVersions.All(remoteVersion => remoteVersion.Key != localVersion.Key) 
                || remoteVersions.KeyVersions.All(remoteVersion => remoteVersion.Key == localVersion.Key && remoteVersion.Version > localVersion.Version)).ToArray();
            
            var toUpsert = remoteVersions.KeyVersions.Where(remoteVersion => localVersions.All(localVersion => localVersion.Key != remoteVersion.Key || localVersion.Version < remoteVersion.Version));

            foreach (var upsertItem in toUpsert)
            {
                if(upsertItem.Value is null)
                {
                    var item = await backupApi.GetObjectAsync(new GetObjectRequest()
                    {
                        Key = upsertItem.Key,
                    });
                    upsertItem.MergeFrom(item.Value);
                }
            }
            
            
            var settingsToDelete = toDelete.Where(key => key.Key.StartsWith("Setting_")).Select(key => key.Key);
            var channelsToDelete = toDelete.Where(key => key.Key.StartsWith("Channel_")).Select(key => key.Key);
            var paymentsToDelete = toDelete.Where(key => key.Key.StartsWith("Payment_")).Select(key => key.Key);
            await db.Settings.Where(setting => settingsToDelete.Contains(setting.EntityKey)).ExecuteDeleteAsync();
            await db.LightningChannels.Where(channel => channelsToDelete.Contains(channel.EntityKey)).ExecuteDeleteAsync();
            await db.LightningPayments.Where(payment => paymentsToDelete.Contains(payment.EntityKey)).ExecuteDeleteAsync();
            
            // upsert the rest when needed
            var settingsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Setting_")).Select(setting=> new Setting()
            {
                Key = setting.Key.Split('_')[1],
                Value = setting.Value.ToByteArray(),
                Version = setting.Version,
                Backup = true
            });
            var channelsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Channel_")).Select(value => JsonSerializer.Deserialize<Channel>(value.Value.ToStringUtf8())!);
            var paymentsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Payment_")).Select(value => JsonSerializer.Deserialize<AppLightningPayment>(value.Value.ToStringUtf8())!);
            
            await db.Settings.UpsertRange(settingsToUpsert).On(setting => setting.EntityKey).RunAsync();
            await db.LightningChannels.UpsertRange(channelsToUpsert).On(channel => channel.EntityKey).RunAsync();
            await db.LightningPayments.UpsertRange(paymentsToUpsert).On(payment => payment.EntityKey).RunAsync();
            
            await db.Database.ExecuteSqlRawAsync(string.Join("; ", triggers.Select(record => record.sql)));
            await db.Database.CommitTransactionAsync();
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await db.Database.RollbackTransactionAsync();
            throw;
        }
    }

}