using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BTCPayApp.Core.Auth;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.VSS;
using Google.Protobuf;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using VSSProto;

namespace BTCPayApp.Core.Backup;

public class SyncService : IDisposable
{
    private readonly ConfigProvider _configProvider;
    private readonly ILogger<SyncService> _logger;
    private readonly IAccountManager _accountManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly ISecureConfigProvider _secureConfigProvider;
    public AsyncEventHandler? EncryptionKeyChanged;    
    public AsyncEventHandler<(List<Outbox> OutboxItemsProcesed, PutObjectRequest RemoteRequest)>? RemoteObjectUpdated;
    public AsyncEventHandler<string[]>? LocalUpdated;
    
    private (Task syncTask, CancellationTokenSource cts, bool local)? _syncTask;

    public SyncService(
        ConfigProvider configProvider,
        ILogger<SyncService> logger,
        ISecureConfigProvider secureConfigProvider,
        IAccountManager accountManager,
        IHttpClientFactory httpClientFactory,
        IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _configProvider = configProvider;
        _logger = logger;
        _accountManager = accountManager;
        _httpClientFactory = httpClientFactory;
        _dbContextFactory = dbContextFactory;
        _secureConfigProvider = secureConfigProvider;
    }
    
    public async Task<string?> GetEncryptionKey()
    {
       return  await _secureConfigProvider.Get<string>("encryptionKey");
    }

    private async Task<IDataProtector?> GetDataProtector()
    {
        var key = await GetEncryptionKey();
        return string.IsNullOrEmpty(key) ? null : new SingleKeyDataProtector(Convert.FromHexString(key));
    }


    public async Task<bool> EncryptionKeyRequiresImport()
    {
        var dataProtector = await GetDataProtector();

        if (dataProtector is not null)
            return false;
        var api = await GetUnencryptedVSSAPI();
        try
        {
            var res = await api.GetObjectAsync(new GetObjectRequest()
            {
                Key = "encryptionKeyTest"
            });

            if (res.Value is null or {Value.Length: 0})
                return false;

            if (dataProtector is null)
                return true;
            var decrypted = dataProtector.Unprotect(res.Value.ToByteArray());
            return "kukks" == Encoding.UTF8.GetString(decrypted);
        }
        catch (VssClientException e) when (e.Error.ErrorCode == ErrorCode.NoSuchKeyException)
        {
            return false;
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while checking if encryption key requires import");
            throw;
        }

    }

    public async Task<bool> SetEncryptionKey(Mnemonic mnemonic)
    {
        var key = mnemonic.DeriveExtKey().Derive(1337).PrivateKey.ToBytes();
        return await SetEncryptionKey(Convert.ToHexString(key));
    }

    public async Task<bool> SetEncryptionKey(string key)
    {
        if (key.Contains(' '))
        {
          return await SetEncryptionKey(new Mnemonic(key));
        }
        var dataProtector = new SingleKeyDataProtector(Convert.FromHexString(key));
        var encrypted = dataProtector.Protect("kukks"u8.ToArray());
        var api = await GetUnencryptedVSSAPI();

        try
        {
            var res = await api.GetObjectAsync(new GetObjectRequest()
            {
                Key = "encryptionKeyTest"
            });

            if (res.Value is {Value.Length: > 0})
            { 
                var decrypted = dataProtector.Unprotect(res.Value.Value.ToByteArray());
                if ("kukks" == Encoding.UTF8.GetString(decrypted))
                {
                    await _secureConfigProvider.Set("encryptionKey", key);
                    EncryptionKeyChanged?.Invoke(this);
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        catch (VssClientException e) when (e.Error.ErrorCode == ErrorCode.NoSuchKeyException)
        {
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error while setting encryption key");
            return false;
        }

        await api.PutObjectAsync(new PutObjectRequest()
        {
            GlobalVersion = await _configProvider.GetDeviceIdentifier(),
            TransactionItems =
            {
                new KeyValue()
                {
                    Key = "encryptionKeyTest",
                    Value = ByteString.CopyFrom(encrypted)
                }
            },
        });
        await _secureConfigProvider.Set("encryptionKey", key);
        EncryptionKeyChanged?.Invoke(this);
        return true;
    }

    private async Task<IVSSAPI> GetUnencryptedVSSAPI()
    {
        var account = _accountManager.GetAccount();
        if (account is null)
            throw new InvalidOperationException("Account not found");
        var vssUri = new Uri(new Uri(account.BaseUri), "vss/");
        var httpClient = _httpClientFactory.CreateClient("vss");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", account.AccessToken);
        var vssClient = new HttpVSSAPIClient(vssUri, httpClient);

        return new AccountAwareVssClient(vssClient, _accountManager);
    }

    private async Task<IVSSAPI?> GetVSSAPI()
    {
        var dataProtector = await GetDataProtector();
        return dataProtector is null ? null : new VSSApiEncryptorClient(await GetUnencryptedVSSAPI(), dataProtector);
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
        if (backupApi is null)
            return;
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
                localVersion.Key != remoteVersion.Key || localVersion.Version < remoteVersion.Version)).Where(value => value.Key != "encryptionKeyTest").ToArray();

            if (toDelete.Length == 0 && !toUpsert.Any())
                return;
            _logger.LogInformation("Syncing to local: {ToDelete} to delete, {ToUpsert} to upsert", toDelete.Length,
                toUpsert.Count());

            foreach (var upsertItem in toUpsert)
            {
                if (upsertItem.Value is null or {Length: 0})
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
            var deleteCount = 0;
            deleteCount += await db.Settings.Where(setting => settingsToDelete.Contains(setting.EntityKey))
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);
            deleteCount += await db.LightningChannels.Where(channel => channelsToDelete.Contains(channel.EntityKey))
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);
            deleteCount += await db.LightningPayments.Where(payment => paymentsToDelete.Contains(payment.EntityKey))
                .ExecuteDeleteAsync(cancellationToken: cancellationToken);

            // upsert the rest when needed
            var settingsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Setting_")).Select(setting => new Setting()
            {
                Key = setting.Key.Replace("Setting_", ""),
                Value = setting.Value.ToByteArray(),
                Version = setting.Version,
                Backup = true
            }).ToArray();
            var channelsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Channel_"))
                .Select(value => JsonSerializer.Deserialize<Channel>(value.Value.ToStringUtf8())!);
            var paymentsToUpsert = toUpsert.Where(key => key.Key.StartsWith("Payment_")).Select(value =>
                JsonSerializer.Deserialize<AppLightningPayment>(value.Value.ToStringUtf8())!);
            var upsertCount = 0;
            upsertCount += await db.Settings.UpsertRange(settingsToUpsert).On(setting => setting.EntityKey)
                .RunAsync(cancellationToken);
            upsertCount += await db.LightningChannels.UpsertRange(channelsToUpsert).On(channel => channel.EntityKey)
                .RunAsync(cancellationToken);
            upsertCount += await db.LightningPayments.UpsertRange(paymentsToUpsert).On(payment => payment.EntityKey)
                .RunAsync(cancellationToken);

            await db.Database.ExecuteSqlRawAsync(string.Join("; ", triggers.Select(record => record.sql)),
                cancellationToken: cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await db.Database.CommitTransactionAsync(cancellationToken);
            _logger.LogInformation("Synced to local: {DeleteCount} deleted, {UpsertCount} upserted", deleteCount,
                upsertCount);
            LocalUpdated?.Invoke(this, toDelete.Concat(toUpsert).Select(key => key.Key).ToArray());
            settingsToUpsert.Select(setting => setting.Key).Concat(settingsToDelete).Distinct().ToList()
                .ForEach(key => _configProvider.Updated?.Invoke(this, key));
        }
        catch (Exception e)
        {
            await db.Database.RollbackTransactionAsync(cancellationToken);
            _logger.LogError(e, "Error while syncing to local");
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

    private SemaphoreSlim _syncLock = new(1, 1);
    public async Task SyncToRemote(CancellationToken cancellationToken = default)
    {
        try
        {
            await _syncLock.WaitAsync(cancellationToken);
     
        var backupAPi = await GetVSSAPI();
        if (backupAPi is null)
            return;
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var putObjectRequest = new PutObjectRequest
        {
            GlobalVersion = await _configProvider.GetDeviceIdentifier()
        };
        var outbox = await db.OutboxItems.GroupBy(outbox1 => outbox1.Key)
            .ToListAsync(cancellationToken: cancellationToken);
        if (outbox.Count != 0)
        {
          
        _logger.LogInformation($"Syncing to remote {outbox.Count} outbox items");
           
        }
        var removedOutboxItems = new List<Outbox>();
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
            removedOutboxItems.AddRange(orderedEnumerable);
            // Process outbox item
        }

        if (putObjectRequest.TransactionItems.Count == 0 && putObjectRequest.DeleteItems.Count == 0 && (_syncTask is not null))
        {
            return;
        }
        await backupAPi.PutObjectAsync(putObjectRequest, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            $"Synced to remote {putObjectRequest.TransactionItems.Count} items and deleted {putObjectRequest.DeleteItems.Count} items" +
            string.Join(", ", putObjectRequest.TransactionItems.Select(kv => kv.Key + " " + kv.Version)));
        RemoteObjectUpdated?.Invoke(this, (removedOutboxItems, putObjectRequest.Clone()));
        }
        finally
        {
            _syncLock.Release();
        }
    }
    public async Task StartSync(bool local,CancellationToken cancellationToken = default)
    {
        if (_syncTask.HasValue && _syncTask.Value.local == local && !_syncTask.Value.cts.IsCancellationRequested)
            return;
        if (_syncTask.HasValue && _syncTask.Value.local != local)
        {
            await _syncTask.Value.cts.CancelAsync();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _syncTask = (ContinuouslySync( local, cts.Token), cts, local);
    }

    public async Task StopSync()
    {
        if (_syncTask.HasValue)
        {
            await _syncTask.Value.cts.CancelAsync();
            _syncTask = null;
        }
        
    }

    private async Task ContinuouslySync(bool local,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (local)
                    await SyncToLocal(cancellationToken);
                else
                    await SyncToRemote( cancellationToken);
                await Task.Delay(2000, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error while syncing to {Local}", local ? "local" : "remote");
            }
        }
    }

    public void Dispose()
    {
        RemoteObjectUpdated = null;
        EncryptionKeyChanged = null;
        LocalUpdated = null;
    }
}