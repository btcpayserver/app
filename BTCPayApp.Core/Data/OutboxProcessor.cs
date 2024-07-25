using System.Text;
using System.Text.Json;
using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Helpers;
using Google.Protobuf;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using VSSProto;

namespace BTCPayApp.Core.Data;

using System;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

public class SingleKeyDataProtector : IDataProtector
{
    private readonly byte[] _key;

    public SingleKeyDataProtector(byte[] key)
    {
        if (key.Length != 32) // AES-256 key size
        {
            throw new ArgumentException("Key length must be 32 bytes.");
        }

        _key = key;
    }

    public IDataProtector CreateProtector(string purpose)
    {
        using var hmac = new HMACSHA256(_key);
        var purposeBytes = Encoding.UTF8.GetBytes(purpose);
        var key = hmac.ComputeHash(purposeBytes).Take(32).ToArray();
        return new SingleKeyDataProtector(key);
    }

    public byte[] Protect(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        byte[] iv = aes.IV;
        byte[] encrypted = aes.EncryptCbc(plaintext, iv);

        byte[] result = new byte[iv.Length + encrypted.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(encrypted, 0, result, iv.Length, encrypted.Length);

        return result;
    }

    public byte[] Unprotect(byte[] protectedData)
    {
        using var aes = Aes.Create();
        aes.Key = _key;

        byte[] iv = new byte[16];
        byte[] cipherText = new byte[protectedData.Length - iv.Length];

        Buffer.BlockCopy(protectedData, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(protectedData, iv.Length, cipherText, 0, cipherText.Length);

        return aes.DecryptCbc(cipherText, iv);
    }

}


public class OutboxProcessor : IScopedHostedService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _btcPayConnectionManager;
    private readonly ISecureConfigProvider _secureConfigProvider;

    public OutboxProcessor(IDbContextFactory<AppDbContext> dbContextFactory, BTCPayConnectionManager btcPayConnectionManager, ISecureConfigProvider secureConfigProvider)
    {
        _dbContextFactory = dbContextFactory;
        _btcPayConnectionManager = btcPayConnectionManager;
        _secureConfigProvider = secureConfigProvider;
    }

    private async Task<IDataProtector> GetDataProtector()
    {
        var k = await _secureConfigProvider.Get<string>("encryptionKey");
        if (k == null)
        {
            k = Convert.ToHexString(RandomUtils.GetBytes(32)).ToLowerInvariant();
            await _secureConfigProvider.Set("encryptionKey", k);
        }

        return new SingleKeyDataProtector(Convert.FromHexString(k));
    }
    
    private async Task<KeyValue?> GetValue(AppDbContext dbContext, Outbox outbox)
    {
        var k = await _secureConfigProvider.Get<string>("encryptionKey");
        if (k == null)
        {
            k = Convert.ToHexString(RandomUtils.GetBytes(32)).ToLowerInvariant();
            await _secureConfigProvider.Set("encryptionKey", k);
            
        }
        switch (outbox.Entity)
        {
            case "Setting":
                var setting = await dbContext.Settings.FindAsync(outbox.Key);
                if(setting?.Backup is not true)
                    return null;
                return new KeyValue()
                {
                    Key = outbox.Key,
                    Value = ByteString.CopyFrom(setting.Value),
                    Version = setting.Version
                };
            case "Channel":
                var channel = await dbContext.LightningChannels.Include(channel1 => channel1.Aliases).SingleOrDefaultAsync(channel1 => channel1.Id == outbox.Key);
                
                if(channel == null)
                    return null;
                var val = JsonSerializer.SerializeToUtf8Bytes(channel);
                return new KeyValue()
                {
                    Key = outbox.Key,
                    Value = ByteString.CopyFrom(val),
                    Version = channel.Version
                };
            case "Payment":
                var split = outbox.Key.Split('_');
                var paymentHash = uint256.Parse(split[0]);
                var paymentId = split[1];
                var inbound = bool.Parse(split[2]);
                
                var payment = await dbContext.LightningPayments.FindAsync(paymentHash, paymentId, inbound);
                if(payment == null)
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
        
    }

    private async Task ProcessOutbox(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        
        var putObjectRequest = new PutObjectRequest();
        var outbox = await db.OutboxItems.GroupBy(outbox1 => new {outbox1.Entity, outbox1.Key})
            .ToListAsync(cancellationToken: cancellationToken);
        foreach (var outboxItemSet in outbox)
        {
            var orderedEnumerable = outboxItemSet.OrderByDescending(outbox1 => outbox1.Version)
                .ThenByDescending(outbox1 => outbox1.ActionType).ToArray();
            foreach (var item in orderedEnumerable)
            {
                if (item.ActionType == OutboxAction.Delete )
                {
                    putObjectRequest.DeleteItems.Add(new KeyValue()
                    {
                        Key = item.Entity, Version = item.Version
                    });
                }
                else
                {
                   var kv = await  GetValue(db, item);
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