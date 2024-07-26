using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using VSSProto;

namespace BTCPayApp.Core.Data;

public class RemoteToLocalSyncService : IScopedHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly IDataProtector _dataProtector;

    public RemoteToLocalSyncService(IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager btcPayConnectionManager, IDataProtectionProvider dataProtectionProvider)
    {
        _dbContextFactory = dbContextFactory;
        _btcPayConnectionManager = btcPayConnectionManager;
        _dataProtector = dataProtectionProvider.CreateProtector("RemoteToLocalSyncService");
    }

    private async Task<KeyValue[]> CreateLocalVersions(AppDbContext dbContext)
    {
        var settings = dbContext.Settings.Where(setting => setting.Backup).Select(setting => new KeyValue()
        {
            Key = "Setting_" + setting.Key,
            Version = setting.Version
        });
        var channels = dbContext.LightningChannels.Select(channel => new KeyValue()
        {
            Key = "Channel_" + channel.Id,
            Version = channel.Version
        });
        var payments = dbContext.LightningPayments.Select(payment => new KeyValue()
        {
            Key = $"Payment_{payment.PaymentHash}_{payment.PaymentId}_{payment.Inbound}",
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
            await db.Database.ExecuteSqlRawAsync("//sql to disable all triggers");
            //   
            //   // see which ones to delete
            //   var toDelete = localVersions.Where(localVersion =>
            //       remoteVersions.KeyVersions(remoteVersion => remoteVersion.Key != localVersion.Key)).ToArray();
            //
            //   var settingsToDelete = toDelete.Where(key => key.Key.StartsWith("Setting_")).Select(key => key.Key.Split('_')[1]);
            //   var channelsToDelete = toDelete.Where(key => key.Key.StartsWith("Channel_")).Select(key => key.Key.Split('_')[1]);
            //   var paymentsToDelete = toDelete.Where(key => key.Key.StartsWith("Payment_")).Select(key =>
            //   {
            //       var keys =  key.Key.Split('_').Skip(1).ToArray();
            //       return (uint256.Parse(keys[0]), keys[1], bool.Parse(keys[2]));
            //   });
            // await db.Settings.Where(setting => settingsToDelete.Contains(setting.Key)).ExecuteDeleteAsync();
            // await db.LightningChannels.Where(channel => channelsToDelete.Contains(channel.Key)).ExecuteDeleteAsync();
            //   await db.LightningPayments.Where(payment => paymentsToDelete.Contains((payment.PaymentHash, payment.PaymentId, payment.Inbound))).ExecuteDeleteAsync();
            //
            //
            //
            // foreach (var key in toDelete)
            // {
            //     if (key.Key.StartsWith("Setting_"))
            //     {
            //         var settingKey = key.Key.Split('_')[1];
            //         var setting = await db.Settings.FindAsync(settingKey);
            //         if (setting != null)
            //         {
            //             db.Settings.Remove(setting);
            //         }
            //     }
            //     else if (key.Key.StartsWith( "Channel_"))
            //     {
            //         var channelId = key.Key.Split('_')[1];
            //         var channel = await db.LightningChannels.FindAsync(channelId);
            //         if (channel != null)
            //         {
            //             db.LightningChannels.Remove(channel);
            //         }
            //     }
            //     else if (key.Key.StartsWith( "Payment_"))
            //     {
            //         var split = key.Key.Split('_');
            //         var paymentHash = uint256.Parse(split[1]);
            //         var paymentId = split[2];
            //         var inbound = bool.Parse(split[3]);
            //         var payment = await db.LightningPayments.FindAsync(paymentHash, paymentId, inbound);
            //         if (payment != null)
            //         {
            //             db.LightningPayments.Remove(payment);
            //         }
            //     }
            //     else
            //     {
            //         throw new ArgumentOutOfRangeException();
            //     }
            // }
            // var toUpdate = localVersions.Where(localVersion =>
            //     remoteVersions.KeyVersions(remoteVersion => remoteVersion.Key > localVersion.Key)).ToArray();
            // // upsert the rest when needed
            // foreach (var remoteVersion in remoteVersions.KeyVersions)
            // {
            //     var localVersion = localVersions.FirstOrDefault(localVersion => localVersion.Key == remoteVersion.Key);
            //     if (localVersion == null || localVersion.Version < remoteVersion.Version)
            //     {
            //         var kv = await backupApi.GetObjectAsync(new GetObjectRequest()
            //         {
            //             Key = remoteVersion.Key
            //         });
            //         if (kv != null)
            //         {
            //             if (remoteVersion.Key.StartsWith("Setting_"))
            //             {
            //                 var settingKey = remoteVersion.Key.Split('_')[1];
            //                 var setting = await db.Settings.FindAsync(settingKey);
            //                 if (setting == null)
            //                 {
            //                     setting = new Setting()
            //                     {
            //                         Key = settingKey
            //                     };
            //                     db.Settings.Add(setting);
            //                 }
            //
            //                 setting.Value = kv.Value.ToByteArray();
            //                 setting.Version = kv.Version;
            //             }
            //             else if (remoteVersion.Key.StartsWith("Channel_"))
            //             {
            //                 var channelId = remoteVersion.Key.Split('_')[1];
            //                 var channel = await db.LightningChannels.FindAsync(channelId);
            //                 if (channel == null)
            //                 {
            //                     channel = new LightningChannel()
            //                     {
            //                         Id = channelId
            //                     };
            //                     db.LightningChannels.Add(channel);
            //                 }
            //
            //                 var channelData = JsonSerializer.Deserialize<LightningChannel>(kv.Value.ToStringUtf8());
            //                 channel.Aliases = channelData.Aliases;
            //                 channel.Version = kv.Version;
            //             }
            //             else if (remoteVersion.Key.StartsWith("Payment_"))
            //             {
            //                 var split = remoteVersion.Key.Split('_');
            //                 var paymentHash = uint256.Parse(split[1]);
            //                 var paymentId = split[2];
            //                 var inbound = bool.Parse(split[3]);
            //                 var payment = await db.LightningPayments.FindAsync(paymentHash, paymentId, inbound);
            //                 if (payment == null)
            //                 {
            //                     payment = new LightningPayment()
            //                     {
            //                         PaymentHash = paymentHash,
            //                         PaymentId = paymentId,
            //                         Inbound = inbound
            //                     };
            //                     db.LightningPayments.Add(payment);
            //                 }
            //
            //                 var paymentData = JsonSerializer.Deserialize<LightningPayment>(kv.Value.ToStringUtf8());
            //                 payment.Amount = paymentData.Amount;
            //                 payment.Bolt11 = paymentData.Bolt11;
            //                 payment.Version = kv.Version;
            //             }
            //             else
            //             {
            //                 throw new ArgumentOutOfRangeException();
            //             }
            //         }
            //     }
            // }
            //
            //
            await db.Database.ExecuteSqlRawAsync("//sql to reenable  all triggers");
            await db.Database.CommitTransactionAsync();
        }
        catch (Exception e)
        {
            await db.Database.RollbackTransactionAsync();
            throw;
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}