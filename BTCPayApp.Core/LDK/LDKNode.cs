using System.Text.Json;
using AsyncKeyedLock;
using BTCPayApp.Core.BTCPayServer;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LSP.JIT;
using BTCPayApp.Core.Wallet;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;
using UInt128 = org.ldk.util.UInt128;

namespace BTCPayApp.Core.LDK;

public partial class LDKNode :
    ILDKEventHandler<Event.Event_ChannelClosed>,
    ILDKEventHandler<Event.Event_ChannelPending>,
    ILDKEventHandler<Event.Event_ChannelReady>
{
    public async Task<IEnumerable<(Channel channel, ChannelDetails? channelDetails)>?> GetChannels(CancellationToken cancellationToken = default)
    {
        return (await _memoryCache.GetOrCreateAsync(nameof(GetChannels), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            await using var dbContext =  await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            var dbChannels = dbContext.LightningChannels.AsNoTracking()
                .Include(channel => channel.Aliases).AsAsyncEnumerable();
            var channels = ServiceProvider.GetRequiredService<ChannelManager>().list_channels();

            var result = new List<(Channel channel, ChannelDetails? channelDetails)>();
            await foreach (var dbChannel in dbChannels)
            {
                var channel = channels.FirstOrDefault(channel =>
                {
                    var id = Convert.ToHexString(channel.get_channel_id().get_a()).ToLowerInvariant();
                    return id == dbChannel.Id || dbChannel.Aliases.Any(alias => alias.Id == id);
                });
                result.Add((dbChannel, channel));
            }
            return result;
        }).WithCancellation(cancellationToken))!;
    }

    public async Task Handle(Event.Event_ChannelClosed evt)
    {
        _logger.LogInformation("Channel {ChannelId} closed: {Reason}", Convert.ToHexString(evt.channel_id.get_a()).ToLowerInvariant(), evt.GetReason());
        await AddChannelData(evt.channel_id, new Dictionary<string, JsonElement>()
        {
            {"CloseReason", JsonSerializer.SerializeToElement(evt.reason.write())},
            {"CloseReasonHuman", JsonSerializer.SerializeToElement(evt.GetReason())},
            {"CloseTimestamp", JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToUnixTimeSeconds())}
        });
        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task Handle(Event.Event_ChannelPending evt)
    {
        await AddChannelData(evt.channel_id, new Dictionary<string, JsonElement>()
        {
            {"PendingTimestamp", JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToUnixTimeSeconds())}
        });

        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task Handle(Event.Event_ChannelReady evt)
    {
        await AddChannelData(evt.channel_id, new Dictionary<string, JsonElement>()
        {
            {"ReadyTimestamp", JsonSerializer.SerializeToElement(DateTimeOffset.UtcNow.ToUnixTimeSeconds())}
        });
        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task<PeerDetails[]> GetPeers(CancellationToken cancellationToken = default)
    {
        return await _memoryCache.GetOrCreateAsync(nameof(GetPeers), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return ServiceProvider.GetRequiredService<PeerManager>().list_peers();
        }).WithCancellation(cancellationToken);
    }

    public void PeersChanged()
    {
        _memoryCache.Remove(nameof(GetPeers));
    }

    private void InvalidateCache()
    {
        _memoryCache.Remove(nameof(GetPeers));
        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task<Result_ChannelIdAPIErrorZ> OpenChannel(Money amount, PubKey nodeId)
    {
        _logger.LogInformation("Opening channel with {NodeId} for {Amount}", nodeId, amount);

        var channelManager = ServiceProvider.GetRequiredService<ChannelManager>();
        var entropySource = ServiceProvider.GetRequiredService<EntropySource>();
        var userConfig = ServiceProvider.GetRequiredService<UserConfig>().clone();
        var temporaryChannelId = ChannelId.temporary_from_entropy_source(entropySource);
        var userChannelId = new UInt128(temporaryChannelId.get_a().Take(16).ToArray());
        var result = await AsyncExtensions.RunInOtherThread(() => channelManager.create_channel(nodeId.ToBytes(),
            amount.Satoshi,
            0,
            userChannelId,
            temporaryChannelId,
            userConfig));

        if (result.is_ok())
            _logger.LogInformation("Opened channel with {NodeId} for {Amount}", nodeId, amount);
        else if (result is Result_ChannelIdAPIErrorZ.Result_ChannelIdAPIErrorZ_Err e && e.err.GetError() is var message)
            _logger.LogError("Opening channel with {NodeId} for {Amount} failed: {Message}", nodeId, amount, message);
        return result;
    }

    public async Task<Result_NoneAPIErrorZ> CloseChannel(ChannelId channelId, PubKey counterparty, bool force)
    {
        var chanId = Convert.ToHexString(channelId.get_a()).ToLowerInvariant();
        _logger.LogInformation("{Action} channel {ChannelId} with {Counterparty}", force ? "Force-closing" : "Closing", chanId, counterparty);

        var channelManager = ServiceProvider.GetRequiredService<ChannelManager>();
        var result = await AsyncExtensions.RunInOtherThread(() => force
                    ? channelManager.force_close_broadcasting_latest_txn(channelId, counterparty.ToBytes(), "User-initiated force-close in unconnected channel state")
                    : channelManager.close_channel(channelId, counterparty.ToBytes()));
        if (result.is_ok())
            _logger.LogInformation("Channel {ChannelId} {Action} with {Counterparty}", chanId, counterparty, force ? "force closed" : "closed");
        if (result is Result_NoneAPIErrorZ.Result_NoneAPIErrorZ_Err e && e.err.GetError() is var message)
            _logger.LogError("{Action} channel {ChannelId} with {Counterparty} failed: {Message}", force ? "Force-closing" : "Closing", chanId, counterparty, message);
        return result;
    }

    public async Task<IJITService?> GetJITLSPService()
    {
        var config = await GetConfig();
        var lsp = config.JITLSP;
        if (string.IsNullOrEmpty(lsp)) return null;

        var jits = ServiceProvider.GetServices<IJITService>();
        return jits.FirstOrDefault(jit => jit.ProviderName == lsp);
    }
}

public partial class LDKNode : IAsyncDisposable, IHostedService, IDisposable
{
    private readonly IMemoryCache _memoryCache;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _connectionManager;
    private readonly ILogger _logger;
    private readonly ConfigProvider _configProvider;
    private readonly OnChainWalletManager _onChainWalletManager;

    public LDKNode(
        IMemoryCache cache,
        IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager connectionManager,
        IServiceProvider serviceProvider,
        LDKWalletLogger logger,
        ConfigProvider configProvider,
        OnChainWalletManager onChainWalletManager)
    {
        _memoryCache = cache;
        _dbContextFactory = dbContextFactory;
        _connectionManager = connectionManager;
        _logger = logger;
        _configProvider = configProvider;
        _onChainWalletManager = onChainWalletManager;
        ServiceProvider = serviceProvider;
    }

    private IServiceProvider ServiceProvider { get; }
    private TaskCompletionSource? _started;
    private readonly SemaphoreSlim _semaphore = new(1);

    public IServiceProvider GetServiceProvider() => ServiceProvider;

    public Network Network => ServiceProvider.GetRequiredService<Network>();

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            exists = _started is not null;
            _started ??= new TaskCompletionSource();
        }
        finally
        {
            _semaphore.Release();
        }

        if (exists)
        {
            await _started.Task;
            return;
        }

        var bb = await _onChainWalletManager.GetBestBlock();
        if (bb is null)
        {
            throw new InvalidOperationException("Best block could not be retrieved. Killing the startup");
        }

        InvalidateCache();
        var walletConfig = await _onChainWalletManager.GetConfig();
        var lightningConfig = await _configProvider.Get<LightningConfig>(key: LightningConfig.Key) ?? new LightningConfig();
        var keyPath = KeyPath.Parse(lightningConfig.LightningDerivationPath);
        Seed = new Mnemonic(walletConfig.Mnemonic).DeriveExtKey().Derive(keyPath).PrivateKey.ToBytes();
        var services = ServiceProvider.GetServices<IScopedHostedService>();

        _logger.LogInformation("Starting LDKNode services");
        foreach (var service in services)
        {
            _logger.LogInformation("Starting {Name}", service.GetType().Name);
            await service.StartAsync(cancellationToken);
        }

        _started.SetResult();
        _logger.LogInformation("LDKNode started");
    }
    //
    // private Task Updated(object? sender, string e)
    // {
    //     if(e == LightningConfig.Key)
    //     {
    //         _ = GetConfig().ContinueWith(config =>
    //         {
    //             _config = config.Result;
    //             ConfigUpdated?.Invoke(this, _config);
    //         });
    //     }
    // }
    //
    // private readonly TaskCompletionSource _configLoaded = new();

    public async Task<LightningConfig> GetConfig()
    {
        return await _configProvider.Get<LightningConfig>(LightningConfig.Key) ?? new LightningConfig();
    }

    public Task<string[]> GetJITLSPs()
    {
        return Task.FromResult(ServiceProvider.GetServices<IJITService>().Select(jit => jit.ProviderName).ToArray());
    }

    public async Task UpdateConfig(Func<LightningConfig, Task<(LightningConfig, bool)>> config)
    {
        await _started.Task;
        await _semaphore.WaitAsync();
        try
        {
            var current = await GetConfig();
            var updated = await config(current);

            if (!updated.Item2)
            {
                return;
            }
            await _configProvider.Set(LightningConfig.Key, updated.Item1, true);


            ConfigUpdated?.Invoke(this, updated.Item1);
        }
        finally
        {

            _semaphore.Release();
        }
    }

    public AsyncEventHandler<LightningConfig>? ConfigUpdated;

    public byte[] Seed { get; private set; }

    public PaymentsManager? PaymentsManager => ServiceProvider.GetRequiredService<PaymentsManager>();
    public LightningAPIKeyManager? ApiKeyManager => ServiceProvider.GetRequiredService<LightningAPIKeyManager>();
    public LDKPeerHandler PeerHandler => ServiceProvider.GetRequiredService<LDKPeerHandler>();
    public PubKey NodeId => new(ServiceProvider.GetRequiredService<ChannelManager>().get_our_node_id());

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            await _semaphore.WaitAsync(cancellationToken);
            exists = _started is not null;
        }
        finally
        {
            _semaphore.Release();
        }

        if (!exists)
            return;
        // var identifier = _onChainWalletManager.WalletConfig.Derivations[WalletDerivation.LightningScripts].Identifier;


        _logger.LogInformation("Stopping LDKNode services");
        var services = ServiceProvider.GetServices<IScopedHostedService>();
        var tasks = services.Select(async service =>
        {
            _logger.LogInformation("Stopping {Name}", service.GetType().Name);
            await service.StopAsync(cancellationToken);
            _logger.LogInformation("Stopped {Name}", service.GetType().Name);
        }).ToArray();
        await Task.WhenAll(tasks);
        // _configProvider.Updated -= Updated;
        // _ = _connectionManager.HubProxy.DeviceMasterSignal(identifier, false).RunSync();
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        // await StopAsync(CancellationToken.None);
    }

    private readonly TaskCompletionSource<ChannelMonitor[]?> icm = new();
    // private LightningConfig? _config;

    public async Task<ChannelMonitor[]> GetInitialChannelMonitors()
    {
        return await icm.Task;
    }

    private async Task<ChannelMonitor[]> GetInitialChannelMonitors(EntropySource entropySource,
        SignerProvider signerProvider)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var data = await db.LightningChannels
            .Where(channel => !channel.Archived && channel.Data != null && channel.Data.Length > 0)
            .Select(channel => channel.Data!)
            .ToArrayAsync();

        var channels = ChannelManagerHelper.GetInitialMonitors(data, entropySource, signerProvider);
        icm.SetResult(channels);
        return channels;
    }


    public async Task<byte[]?> GetRawChannelManager()
    {
        return await _configProvider.Get<byte[]>("ln:ChannelManager") ?? null;
    }

    public async Task UpdateChannelManager(ChannelManager serializedChannelManager)
    {
        await _configProvider.Set("ln:ChannelManager", serializedChannelManager.write(), true);
    }

    public async Task UpdateNetworkGraph(NetworkGraph networkGraph)
    {
        await _configProvider.Set("ln:NetworkGraph", networkGraph.write(), true);
    }

    public async Task UpdateScore(WriteableScore score)
    {
        await _configProvider.Set("ln:Score", score.write(), true);
    }

    public async Task<(byte[] serializedChannelManager, ChannelMonitor[] channelMonitors)?> GetSerializedChannelManager(
        EntropySource entropySource, SignerProvider signerProvider)
    {
        var data = await GetRawChannelManager();
        if (data is null)
        {
            icm.SetResult([]);
            return null;
        }

        var channels = await GetInitialChannelMonitors(entropySource, signerProvider);
        return (data, channels);
    }

    public async Task<Script> DeriveScript()
    {
        var derivationKey = (await GetConfig()).ScriptDerivationKey;
        return (await _onChainWalletManager.DeriveScript(derivationKey)).ScriptPubKey;
    }

    public Task<string?> Identifier => _onChainWalletManager.GetConfig().ContinueWith(task => task.Result?.Derivations[WalletDerivation.LightningScripts].Identifier);


    public async Task TrackScripts(Script[] scripts, string derivation = WalletDerivation.LightningScripts)
    {
        try
        {
            _logger.LogDebug("Tracking scripts {Scripts}", string.Join(",", scripts.Select(script => script.ToHex())));
            var config = await _onChainWalletManager.GetConfig();
            var identifier = config.Derivations[derivation].Identifier;

            await _connectionManager.HubProxy.TrackScripts(identifier,
                scripts.Select(script => script.ToHex()).ToArray()).RunInOtherThread();
            _logger.LogDebug("Tracked scripts {Scripts}", string.Join(",", scripts.Select(script => script.ToHex())));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error tracking scripts {Scripts}",
                string.Join(",", scripts.Select(script => script.ToHex())));
        }
    }
    AsyncKeyedLocker<string> channelLocker = new();

    public async Task UpdateChannel(List<ChannelAlias> identifiers, byte[] write, long checkpoint)
    {
        using var releaser = await channelLocker.LockAsync(identifiers.First().Id);
        //TODO: convert to upsert and ensure aliases are saved with upsert too
        var ids = identifiers.Select(alias => alias.Id).ToArray();
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var channel = (await context.ChannelAliases.Include(alias => alias.Channel)
            .ThenInclude(channel1 => channel1.Aliases).FirstOrDefaultAsync(alias => ids.Contains(alias.Id)))?.Channel;

        if (channel is not null)
        {
            foreach (var alias in identifiers)
            {
                alias.ChannelId = channel.Id;
                if (channel.Aliases.All(a => a.Id != alias.Id))
                {
                    channel.Aliases.Add(alias);
                }
            }

            channel.Data = write;
            channel.Checkpoint = checkpoint;
        }
        else
        {
            channel = new Channel
            {
                Id = ids.First(),
                Data = write,
                Aliases = identifiers.ToList(),
                Checkpoint = checkpoint
            };

            await context.LightningChannels.AddAsync(channel);
            foreach (var alias in identifiers)
            {
                alias.ChannelId = channel.Id;
            }
        }

        _logger.LogDebug("Updating channel {ChannelId} with checkpoint {Checkpoint}", channel.Id, checkpoint);
        await context.SaveChangesAsync();
    }

    public async Task Peer(PubKey key, PeerInfo? value)
    {
        var toString = key.ToString().ToLowerInvariant();
        await UpdateConfig(config =>
        {
            if (value is null && config.Peers.Remove(toString))
                return Task.FromResult((config, true));

            config.Peers!.AddOrReplace(toString, value);
            return Task.FromResult((config, true));
        });
    }

    public async Task ArchiveChannel(ChannelId id)
    {
        var channelId = Convert.ToHexString(id.get_a()).ToLowerInvariant();
        using var releaser = await channelLocker.LockAsync(channelId);
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var channel = await context.LightningChannels.Include(channel => channel.Aliases)
            .FirstOrDefaultAsync(channel => !channel.Archived && (channel.Id == channelId || channel.Aliases.Any(alias => alias.Id == channelId)));
        if (channel is null)
        {
            return;
        }

        channel.Archived = true;
        await context.SaveChangesAsync();
    }

    private async Task AddChannelData(ChannelId id, Dictionary<string, JsonElement> data)
    {
        var channelId = Convert.ToHexString(id.get_a()).ToLowerInvariant();
        using var releaser = await channelLocker.LockAsync(channelId);
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var channel = await context.LightningChannels.Include(channel => channel.Aliases)
            .FirstOrDefaultAsync(channel => !channel.Archived && (channel.Id == channelId || channel.Aliases.Any(alias => alias.Id == channelId)));
        if (channel is null)
        {
            channel = new Channel
            {
                Id = channelId,
                Archived = false,
                Data = [],
                Checkpoint = 0
            };

            await context.LightningChannels.AddAsync(channel);
        }

        channel.AdditionalData = JsonSerializer.SerializeToNode(channel.AdditionalData)!.MergeDictionary(data).Deserialize<Dictionary<string, JsonElement>>();
        await context.SaveChangesAsync();
    }
}
