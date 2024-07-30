﻿using System.Collections.Specialized;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayApp.Core.Helpers;
using BTCPayApp.Core.LDK;
using BTCPayApp.Core.LSP.JIT;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using org.ldk.structs;
using OutPoint = NBitcoin.OutPoint;
using UInt128 = org.ldk.util.UInt128;

namespace BTCPayApp.Core.Attempt2;

public partial class LDKNode: 
    ILDKEventHandler<Event.Event_ChannelClosed>,
    ILDKEventHandler<Event.Event_ChannelPending>,
    ILDKEventHandler<Event.Event_ChannelReady>
{

    
    
    
    public async Task<ChannelDetails[]> GetChannels(CancellationToken cancellationToken = default)
    {
        return await _memoryCache.GetOrCreateAsync(nameof(GetChannels),  async entry => 
        {
            
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
            return ServiceProvider.GetRequiredService<ChannelManager>().list_channels();
        }).WithCancellation(cancellationToken);
        
    }


    public async Task Handle(Event.Event_ChannelClosed evt)
    {
        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task Handle(Event.Event_ChannelPending @event)
    {
        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task Handle(Event.Event_ChannelReady @event)
    {
        _memoryCache.Remove(nameof(GetChannels));
    }

    public async Task<PeerDetails[]> GetPeers(CancellationToken cancellationToken = default)
    {
        
        return await _memoryCache.GetOrCreateAsync(nameof(GetPeers),  async entry => 
        {
            
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
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

    public async Task<Result_ChannelIdAPIErrorZ> OpenChannel(Money amount, PubKey nodeId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Opening channel with {nodeId} for {amount}", nodeId, amount);
        
        var channelManager = ServiceProvider.GetRequiredService<ChannelManager>();
        var entropySource = ServiceProvider.GetRequiredService<EntropySource>();
        var userConfig = ServiceProvider.GetRequiredService<UserConfig>();
        

        var temporaryChannelId = ChannelId.temporary_from_entropy_source(entropySource);

            
        var userChannelId = new UInt128(temporaryChannelId.get_a().Take(16).ToArray());
        try
        {
            return await Task.Run(() => channelManager.create_channel(nodeId.ToBytes(), amount.Satoshi, 0, userChannelId,
                temporaryChannelId, userConfig), cancellationToken);
        }
        finally
        {
            
            _logger.LogInformation("finished (trying to) opening channel with {nodeId} for {amount}", nodeId, amount);
        }
       
    }

    public async Task<IJITService?> GetJITLSPService()
    {
        var config = await GetConfig();
        var lsp = config.JITLSP;
        if(lsp is null)
        {
            return null;
        }
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
    private readonly IConfigProvider _configProvider;
    private readonly OnChainWalletManager _onChainWalletManager;

    public LDKNode(
        IMemoryCache cache,
        IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager connectionManager,
        IServiceProvider serviceProvider, 
        LDKWalletLogger logger, 
        IConfigProvider configProvider,
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
        InvalidateCache();
        _config = await _configProvider.Get<LightningConfig>(key: LightningConfig.Key)?? new LightningConfig();
        _configLoaded.SetResult();
        var keyPath = KeyPath.Parse(_config.LightningDerivationPath);
        Seed = new Mnemonic( _onChainWalletManager.WalletConfig.Mnemonic).DeriveExtKey().Derive(keyPath).PrivateKey.ToBytes();
        var services = ServiceProvider.GetServices<IScopedHostedService>();

        _logger.LogInformation("Starting LDKNode services");
        var bb = await _onChainWalletManager.GetBestBlock();
        if (bb is null)
        {
            throw new InvalidOperationException("Best block could not be retrieved. Killing the startup");
        }
        foreach (var service in services)
        {
            _logger.LogInformation($"Starting {service.GetType().Name}");
            await service.StartAsync(cancellationToken);
        }

        _started.SetResult();
        _logger.LogInformation("LDKNode started");
    }

   private readonly TaskCompletionSource _configLoaded = new();
    
    public async Task<LightningConfig> GetConfig()
    {
        await _configLoaded.Task;
        return _config!;
    }
    public async Task<string[]> GetJITLSPs()
    {
       return  ServiceProvider.GetServices<IJITService>().Select(jit => jit.ProviderName).ToArray();
    }

    public async Task UpdateConfig(LightningConfig config)
    {
        await _started.Task;
        await _configProvider.Set(LightningConfig.Key, config, true);
        _config = config;
        
        ConfigUpdated?.Invoke(this, config);
    }
    

    public AsyncEventHandler<LightningConfig>? ConfigUpdated;
    
    public byte[] Seed { get; private set; }

    public PaymentsManager PaymentsManager => ServiceProvider.GetRequiredService<PaymentsManager>();
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
            _logger.LogInformation($"Stopping {service.GetType().Name}");
            await  service.StopAsync(cancellationToken);
            _logger.LogInformation($"Stopped {service.GetType().Name}");
        }).ToArray();
        await Task.WhenAll(tasks);
        // _ = _connectionManager.HubProxy.DeviceMasterSignal(identifier, false).RunSync();
        
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
         // await StopAsync(CancellationToken.None);
    }
    
    
    

    private readonly TaskCompletionSource<ChannelMonitor[]?> icm = new();
    private LightningConfig? _config;

    public async Task<ChannelMonitor[]> GetInitialChannelMonitors()
    {
        return await icm.Task;
    }
    private async Task<ChannelMonitor[]> GetInitialChannelMonitors(EntropySource entropySource,
        SignerProvider signerProvider)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync();

        var data = await db.LightningChannels.Select(channel => channel.Data)
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
            icm.SetResult(Array.Empty<ChannelMonitor>());
            return null;
        }

        var channels = await GetInitialChannelMonitors(entropySource, signerProvider);
        return (data, channels);
    }

    public async Task<Script> DeriveScript()
    {
        var derivationKey = (await GetConfig()).ScriptDerivationKey;
        return await _onChainWalletManager.DeriveScript(derivationKey);
    }


    public async Task TrackScripts(Script[] scripts, string derivation = WalletDerivation.LightningScripts)
    {
        try
        {

            _logger.LogDebug("Tracking scripts {scripts}", string.Join(",", scripts.Select(script => script.ToHex())));
            var identifier = _onChainWalletManager.WalletConfig.Derivations[derivation].Identifier;

            await _connectionManager.HubProxy.TrackScripts(identifier,
                scripts.Select(script => script.ToHex()).ToArray()).RunSync();
            _logger.LogDebug("Tracked scripts {scripts}", string.Join(",", scripts.Select(script => script.ToHex())));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error tracking scripts {scripts}", string.Join(",", scripts.Select(script => script.ToHex())));
        }
    }

    public async Task UpdateChannel(List<ChannelAlias> identifiers, byte[] write)
    {
        var ids = identifiers.Select(alias => alias.Id).ToArray();
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        var channel = (await context.ChannelAliases.Include(alias => alias.Channel)
            .ThenInclude(channel1 => channel1.Aliases).FirstOrDefaultAsync(alias => ids.Contains(alias.Id)))?.Channel;

        if (channel is not null)
        {
            foreach (var alias in identifiers)
            {
                if (channel.Aliases.All(a => a.Id != alias.Id))
                {
                    channel.Aliases.Add(alias);
                }
            }

            channel.Data = write;
        }
        else
        {
            await context.LightningChannels.AddAsync(new Channel()
            {
                Id = identifiers.First().ChannelId,
                Data = write,
                Aliases = identifiers.ToList()
            });
        }
        await context.SaveChangesAsync();
    }


    public async Task Peer(PubKey key, PeerInfo? value)
    {
        var toString = key.ToString().ToLowerInvariant();
        var config = await GetConfig();
        if (value is null)
        {
            if (config.Peers.Remove(toString))
            {
                await UpdateConfig(config);
                return;
            }
        }
        config.Peers.AddOrReplace(toString, value);
        await UpdateConfig(config);
    }
}