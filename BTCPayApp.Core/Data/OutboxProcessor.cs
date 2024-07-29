using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Helpers;
using Google.Protobuf;
using VSSProto;
using Microsoft.EntityFrameworkCore;

namespace BTCPayApp.Core.Data;

using System;
using System.Linq;

public class OutboxProcessor : IScopedHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;

    public OutboxProcessor(IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager btcPayConnectionManager)
    {
        _dbContextFactory = dbContextFactory;
        _btcPayConnectionManager = btcPayConnectionManager;
    }


    private async Task<KeyValue?> GetValue(AppDbContext dbContext, Outbox outbox)
    {
        
        switch (outbox.Entity)
        {
            case "Setting":
                var setting = await dbContext.Settings.SingleOrDefaultAsync(setting1 => setting1.EntityKey == outbox.Key && setting1.Backup);
                if (setting == null)
                    return null;
                return new KeyValue()
                {
                    Key = outbox.Key,
                    Value = ByteString.CopyFrom(setting.Value),
                    Version = setting.Version
                };
            case "Channel":
                var channel = await dbContext.LightningChannels.Include(channel1 => channel1.Aliases)
                    .SingleOrDefaultAsync(channel1 => channel1.EntityKey == outbox.Key);
        
                if (channel == null)
                    return null;
                var val = JsonSerializer.SerializeToUtf8Bytes(channel);
                return new KeyValue()
                {
                    Key = outbox.Key,
                    Value = ByteString.CopyFrom(val),
                    Version = channel.Version
                };
            case "Payment":
                var payment = await dbContext.LightningPayments .SingleOrDefaultAsync(lightningPayment =>  lightningPayment.EntityKey == outbox.Key);
                if (payment == null)
                    return null;
                var paymentBytes = JsonSerializer.SerializeToUtf8Bytes(payment);
                return new KeyValue()
                {
                    Key = outbox.Key,
                    Value = ByteString.CopyFrom(paymentBytes),
                    Version = payment.Version
                };
            default:
                throw new ArgumentOutOfRangeException();
        }
        
        
        // switch (outbox.Entity)
        // {
        //     case "Setting":
        //         var setting = await dbContext.Settings.FindAsync(outbox.Key);
        //         if (setting?.Backup is not true)
        //             return null;
        //         return new KeyValue()
        //         {
        //             Key = "Setting_" + outbox.Key,
        //             Value = ByteString.CopyFrom(setting.Value),
        //             Version = setting.Version
        //         };
        //     case "Channel":
        //         var channel = await dbContext.LightningChannels.Include(channel1 => channel1.Aliases)
        //             .SingleOrDefaultAsync(channel1 => channel1.Id == outbox.Key);
        //
        //         if (channel == null)
        //             return null;
        //         var val = JsonSerializer.SerializeToUtf8Bytes(channel);
        //         return new KeyValue()
        //         {
        //             Key = "Channel_" + outbox.Key,
        //             Value = ByteString.CopyFrom(val),
        //             Version = channel.Version
        //         };
        //     case "Payment":
        //         var split = outbox.Key.Split('_');
        //         var paymentHash = uint256.Parse(split[0]);
        //         var paymentId = split[1];
        //         var inbound = bool.Parse(split[2]);
        //
        //         var payment = await dbContext.LightningPayments.FindAsync(paymentHash, paymentId, inbound);
        //         if (payment == null)
        //             return null;
        //         var paymentBytes = JsonSerializer.SerializeToUtf8Bytes(payment);
        //         return new KeyValue()
        //         {
        //             Key = "Payment_" + outbox.Key,
        //             Value = ByteString.CopyFrom(paymentBytes),
        //             Version = payment.Version
        //         };
        //     default:
        //         throw new ArgumentOutOfRangeException();
        // }
    }

    private async Task ProcessOutbox(CancellationToken cancellationToken = default)
    {
        var backupAPi = await _btcPayConnectionManager.GetVSSAPI();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var putObjectRequest = new PutObjectRequest();
        var outbox = await db.OutboxItems.GroupBy(outbox1 => new {outbox1.Key})
            .ToListAsync(cancellationToken: cancellationToken);
        foreach (var outboxItemSet in outbox)
        {
            var orderedEnumerable = outboxItemSet.OrderByDescending(outbox1 => outbox1.Version)
                .ThenByDescending(outbox1 => outbox1.ActionType).ToArray();
            foreach (var item in orderedEnumerable)
            {
                if (item.ActionType == OutboxAction.Delete)
                {
                    putObjectRequest.DeleteItems.Add(new KeyValue()
                    {
                        Key = item.Key, Version = item.Version
                    });
                }
                else
                {
                    var kv = await GetValue(db, item);
                    if (kv != null)
                    {
                        putObjectRequest.TransactionItems.Add(kv);
                        break;
                    }
                }
            }

            db.OutboxItems.RemoveRange(orderedEnumerable);
            // Process outbox item
        }

        await backupAPi.PutObjectAsync(putObjectRequest, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private CancellationTokenSource _cts = new();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await ProcessOutbox(_cts.Token);
                await Task.Delay(2000, _cts.Token);
            }
        }, _cts.Token);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _cts.CancelAsync();
        await ProcessOutbox(cancellationToken);
    }
}