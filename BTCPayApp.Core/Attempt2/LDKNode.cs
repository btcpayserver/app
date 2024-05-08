using BTCPayApp.Core.Attempt2;
using BTCPayApp.Core.Contracts;
using BTCPayApp.Core.Data;
using BTCPayServer.Lightning;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Scripting;
using nldksample.LDK;
using org.ldk.structs;

namespace BTCPayApp.Core.LDK;

public class LDKNode : IAsyncDisposable, IHostedService, IDisposable
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
    private readonly BTCPayConnectionManager _connectionManager;
    private readonly ILogger _logger;
    private readonly IConfigProvider _configProvider;
    private readonly OnChainWalletManager _onChainWalletManager;

    public LDKNode(
        IDbContextFactory<AppDbContext> dbContextFactory,
        BTCPayConnectionManager connectionManager,
        IServiceProvider serviceProvider, 
        LDKWalletLogger logger, 
        IConfigProvider configProvider,
        OnChainWalletManager onChainWalletManager)
    {
        _dbContextFactory = dbContextFactory;
        _connectionManager = connectionManager;
        _logger = logger;
        _configProvider = configProvider;
        _onChainWalletManager = onChainWalletManager;
        ServiceProvider = serviceProvider;
    }
    
    public IServiceProvider ServiceProvider { get; }
    private TaskCompletionSource? _started;
    private readonly SemaphoreSlim _semaphore = new(1);

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
        Config = await _configProvider.Get<LightningConfig>(key: LightningConfig.Key)?? new LightningConfig();
        var keyPath = KeyPath.Parse(Config.LightningDerivationPath);
        Seed = new Mnemonic( _onChainWalletManager.WalletConfig.Mnemonic).DeriveExtKey().Derive(keyPath).PrivateKey.ToBytes();
        var services = ServiceProvider.GetServices<IScopedHostedService>();

        _logger.LogInformation("Starting LDKNode services");
        foreach (var service in services)
        {
            await service.StartAsync(cancellationToken);
        }

        _started.SetResult();
        _logger.LogInformation("LDKNode started");
    }

    public LightningConfig Config { get; private set; }

    public byte[] Seed { get; private set; }


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
        var identifier = _onChainWalletManager.WalletConfig.Derivations[WalletDerivation.LightningScripts].Identifier;
        await _connectionManager.HubProxy.MasterNodePong(identifier, false);
        
        var services = ServiceProvider.GetServices<IScopedHostedService>();
        foreach (var service in services)
        {
            await service.StopAsync(cancellationToken);
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
         // await StopAsync(CancellationToken.None);
    }
    
    
    

    private readonly TaskCompletionSource<ChannelMonitor[]?> icm = new();

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

    public async Task UpdateConfig(LightningConfig config)
    {
        Config = config;
        await _configProvider.Set(LightningConfig.Key, config);
    }
    
    
    public async Task<byte[]?> GetRawChannelManager()
    {
        return await _configProvider.Get<byte[]>("ChannelManager") ?? null;
    }

    public async Task UpdateChannelManager(ChannelManager serializedChannelManager)
    {
        await _configProvider.Set("ChannelManager", serializedChannelManager.write());
    }

    
    public async Task UpdateNetworkGraph(NetworkGraph networkGraph)
    {
        await _configProvider.Set("NetworkGraph", networkGraph.write());
    }

    public async Task UpdateScore(WriteableScore score)
    {
        await _configProvider.Set("Score", score.write());
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
        return await _onChainWalletManager.DeriveScript(Config.ScriptDerivationKey);
    }

    public async Task TrackScripts(Script[] scripts)
    {
        var identifier = _onChainWalletManager.WalletConfig.Derivations[WalletDerivation.LightningScripts].Identifier;
        
        await _connectionManager.HubProxy.TrackScripts(identifier,scripts.Select(script => script.ToHex()).ToArray());
    }

    public async Task UpdateChannel(string id, byte[] write)
    {
        await using var context = await _dbContextFactory.CreateDbContextAsync();
        
        var channel = await context.LightningChannels.SingleOrDefaultAsync(lightningChannel => lightningChannel.Id == id || lightningChannel.Aliases.Contains(id));

        if (channel is not null)
        {
            if (!channel.Aliases.Contains(channel.Id))
            {
                channel.Aliases.Add(channel.Id);
            }
            if (!channel.Aliases.Contains(id))
            {
                channel.Aliases.Add(id);
            }

            channel.Id = id;
            channel.Data = write;
        }
        else
        {
            await context.LightningChannels.AddAsync(new Channel()
            {
                Id = id,
                Data = write,
                Aliases = [id]
            });
        }
        await context.SaveChangesAsync();
    }
}