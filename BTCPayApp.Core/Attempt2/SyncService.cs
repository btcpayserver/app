﻿using System.Net.Http.Headers;
using System.Text.Json;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.VSS;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using VSSProto;

namespace BTCPayApp.Core.Attempt2;

public class SyncService
{
    private readonly ILogger<SyncService> _logger;
    private readonly IAccountManager _accountManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ISecureConfigProvider _secureConfigProvider;

    public SyncService(
        ILogger<SyncService> logger,
        ISecureConfigProvider secureConfigProvider,
        IAccountManager accountManager,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _logger = logger;
        _accountManager = accountManager;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _secureConfigProvider = secureConfigProvider;
    }

    private async Task<IDataProtector> GetDataProtector()
    {
        var key = await _secureConfigProvider.GetOrSet("encryptionKey",
            async () => Convert.ToHexString(RandomUtils.GetBytes(32)).ToLowerInvariant());
        return new SingleKeyDataProtector(Convert.FromHexString(key));
    }


    private async Task<IVSSAPI> GetVSSAPI()
    {
        var account = _accountManager.GetAccount();
        if (account is null)
            throw new InvalidOperationException("Account not found");
        var vssUri = new Uri(new Uri(account.BaseUri), "vss/");
        var httpClient = _httpClientFactory.CreateClient("vss");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var vssClient = new HttpVSSAPIClient(vssUri, httpClient);
        var protector = await GetDataProtector();
        return new VSSApiEncryptorClient(vssClient, protector);
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

    public async Task SyncToLocal(CancellationToken cancellationToken = default)
    {
        var backupApi = await GetVSSAPI();
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var localVersions = await CreateLocalVersions(db);
        var remoteVersions = await backupApi.ListKeyVersionsAsync(new ListKeyVersionsRequest(), cancellationToken);
        await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var triggers = await db.Database
                .SqlQuery<TriggerRecord>($"SELECT name, sql FROM sqlite_master WHERE type = 'trigger'")
                .ToListAsync(cancellationToken: cancellationToken);
            await db.Database.ExecuteSqlRawAsync(
                string.Join("; ", triggers.Select(trigger => $"DROP TRIGGER IF EXISTS {trigger.name}")),
                cancellationToken: cancellationToken);

            // delete local versions that are not in remote
            // delete local versions which are lower than remote

            var toDelete = localVersions.Where(localVersion =>
                remoteVersions.KeyVersions.All(remoteVersion => remoteVersion.Key != localVersion.Key)
                || remoteVersions.KeyVersions.All(remoteVersion =>
                    remoteVersion.Key == localVersion.Key && remoteVersion.Version > localVersion.Version)).ToArray();

            var toUpsert = remoteVersions.KeyVersions.Where(remoteVersion => localVersions.All(localVersion =>
                localVersion.Key != remoteVersion.Key || localVersion.Version < remoteVersion.Version));

            foreach (var upsertItem in toUpsert)
            {
                if (upsertItem.Value is null)
                {
                    var item = await backupApi.GetObjectAsync(new GetObjectRequest()
                    {
                        Key = upsertItem.Key,
                    }, cancellationToken);
                    upsertItem.MergeFrom(item.Value);
                }
            }


            var settingsToDelete = toDelete.Where(key => key.Key.StartsWith("Setting_")).Select(key => key.Key);
            var channelsToDelete = toDelete.Where(key => key.Key.StartsWith("Channel_")).Select(key => key.Key);
            var paymentsToDelete = toDelete.Where(key => key.Key.StartsWith("Payment_")).Select(key => key.Key);
            await db.Settings.Where(setting => settingsToDelete.Contains(setting.EntityKey))
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);
            await db.LightningChannels.Where(channel => channelsToDelete.Contains(channel.EntityKey))
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);
            await db.LightningPayments.Where(payment => paymentsToDelete.Contains(payment.EntityKey))
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);

            // upsert the rest when needed
            var settingsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Setting_")).Select(setting => new Setting()
            {
                Key = setting.Key.Split('_')[1],
                Value = setting.Value.ToByteArray(),
                Version = setting.Version,
                Backup = true
            });
            var channelsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Channel_"))
                .Select(value => JsonSerializer.Deserialize<Channel>(value.Value.ToStringUtf8())!);
            var paymentsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Payment_")).Select(value =>
                JsonSerializer.Deserialize<AppLightningPayment>(value.Value.ToStringUtf8())!);

            await db.Settings.UpsertRange(settingsToUpsert).On(setting => setting.EntityKey)
                .RunAsync(cancellationToken);
            await db.LightningChannels.UpsertRange(channelsToUpsert).On(channel => channel.EntityKey)
                .RunAsync(cancellationToken);
            await db.LightningPayments.UpsertRange(paymentsToUpsert).On(payment => payment.EntityKey)
                .RunAsync(cancellationToken);

            await db.Database.ExecuteSqlRawAsync(string.Join("; ", triggers.Select(record => record.sql)),
                cancellationToken: cancellationToken);
            await db.Database.CommitTransactionAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception e)
        {
            await db.Database.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }

    private async Task<KeyValue?> GetValue(AppDbContext dbContext, Outbox outbox)
    {
        switch (outbox.Entity)
        {
            case "Setting":
                var setting = await dbContext.Settings.SingleOrDefaultAsync(setting1 =>
                    setting1.EntityKey == outbox.Key && setting1.Backup);
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
                var payment = await dbContext.LightningPayments.SingleOrDefaultAsync(lightningPayment =>
                    lightningPayment.EntityKey == outbox.Key);
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
    }

    public async Task SyncToRemote(long deviceIdentifier, CancellationToken cancellationToken = default)
    {
        var backupAPi = await GetVSSAPI();
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

        putObjectRequest.GlobalVersion = deviceIdentifier;
        await backupAPi.PutObjectAsync(putObjectRequest, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private (Task syncTask, CancellationTokenSource cts, bool local)? _syncTask;

    public async Task StartSync(bool local, long deviceIdentifier, CancellationToken cancellationToken = default)
    {
        if (_syncTask.HasValue && _syncTask.Value.local == local)
            return;
        if (_syncTask.HasValue)
        {
            await _syncTask.Value.cts.CancelAsync();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _syncTask = (ContinuouslySync(deviceIdentifier,local, cts.Token), cts, local);
    }

    public async Task StopSync()
    {
        if (_syncTask.HasValue)
        {
            await _syncTask.Value.cts.CancelAsync();
            _syncTask = null;
        }
    }

    private async Task ContinuouslySync(long deviceIdentifier, bool local, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (local)
                    await SyncToLocal(cancellationToken);
                else
                    await SyncToRemote(deviceIdentifier, cancellationToken);
                await Task.Delay(2000, cancellationToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while syncing to {Local}", local ? "local" : "remote");
            }
        }
    }
}