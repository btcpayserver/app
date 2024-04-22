using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using BTCPayApp.Core.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using nldksample;
using org.ldk.enums;
using org.ldk.structs;
using org.ldk.util;
using WalletWasabi.Userfacing;
using EventHandler = System.EventHandler;
using NodeInfo = BTCPayServer.Lightning.NodeInfo;
using OutPoint = org.ldk.structs.OutPoint;

namespace BTCPayApp.Core.LDK;

public class Extensions
{
}

public interface IScopedHostedService : IHostedService
{
}

public class LDKWalletLoggerFactory : ILoggerFactory
{
    private readonly WalletService _walletService;
    private readonly ILoggerFactory _inner;

    public LDKWalletLoggerFactory(WalletService walletService, ILoggerFactory loggerFactory)
    {
        _walletService = walletService;
        _inner = loggerFactory;
    }

    public void Dispose()
    {
        //ignore as this is scoped
    }

    public void AddProvider(ILoggerProvider provider)
    {
        _inner.AddProvider(provider);
    }

    public List<string> Logs { get; } = new List<string>();

    public ILogger CreateLogger(string category)
    {
        var categoryName = (string.IsNullOrWhiteSpace(category) ? "LDK" : $"LDK.{category}") +
                           $"[{_walletService.CurrentWallet}]";
        LoggerWrapper logger = new LoggerWrapper(_inner.CreateLogger(categoryName));

        logger.LogEvent += (sender, message) =>
            Logs.Add(DateTime.Now.ToShortTimeString() + " " + categoryName + message);

        return logger;
    }
}

public class LoggerWrapper : ILogger
{
    private readonly ILogger _inner;

    public LoggerWrapper(ILogger inner)
    {
        _inner = inner;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _inner.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _inner.IsEnabled(logLevel);
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _inner.Log(logLevel, eventId, state, exception, formatter);
        LogEvent?.Invoke(this, formatter(state, exception));
    }

    public event EventHandler<string>? LogEvent;
}

public class LDKWalletLogger : LDKLogger
{
    public LDKWalletLogger(LDKWalletLoggerFactory ldkWalletLoggerFactory) : base(ldkWalletLoggerFactory)
    {
    }
}

public class LDKLogger : LoggerInterface, ILogger
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _baseLogger;

    public LDKLogger(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _baseLogger = loggerFactory.CreateLogger("");
    }

    public virtual void log(Record record)
    {
        var level = record.get_level() switch
        {
            Level.LDKLevel_Trace => LogLevel.Trace,
            Level.LDKLevel_Debug => LogLevel.Debug,
            Level.LDKLevel_Info => LogLevel.Information,
            Level.LDKLevel_Warn => LogLevel.Warning,
            Level.LDKLevel_Error => LogLevel.Error,
            Level.LDKLevel_Gossip => LogLevel.Trace,
        };
        _loggerFactory.CreateLogger(record.get_module_path()).Log(level, "{Args}", record.get_args());
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        return _baseLogger.BeginScope(state);
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return _baseLogger.IsEnabled(logLevel);
    }

    public virtual void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _baseLogger.Log(logLevel, eventId, state, exception, formatter);
    }
}

public class LDKPeerHandler : IScopedHostedService
{
    private readonly ILogger<LDKPeerHandler> _logger;
    private readonly PeerManager _peerManager;
    private readonly ChannelManager _channelManager;
    private CancellationTokenSource? _cts;

    public event EventHandler<PeersChangedEventArgs> OnPeersChange;

    readonly ObservableConcurrentDictionary<string, LDKTcpDescriptor> _descriptors = new();

    public LDKTcpDescriptor[] ActiveDescriptors => _descriptors.Values.ToArray();

    public LDKPeerHandler(PeerManager peerManager, LDKWalletLoggerFactory logger, ChannelManager channelManager)
    {
        _peerManager = peerManager;
        _channelManager = channelManager;
        _logger = logger.CreateLogger<LDKPeerHandler>();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _ = ListenForInboundConnections(_cts.Token);
        _ = PeriodicTicker(_cts.Token, 1000, () => _peerManager.process_events());
        _descriptors.CollectionChanged += DescriptorsOnCollectionChanged;
    }

    private void DescriptorsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Task.Run(async () =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add)
            {
                await Task.Delay(2000);
            }

            await GetPeerNodeIds();
        });
    }

    private async Task PeriodicTicker(CancellationToken cancellationToken, int ms, Action action)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(ms, cancellationToken);
            action.Invoke();
        }
    }

    private async Task PeriodicTickerAsync(CancellationToken cancellationToken, int ms, Func<Task> action)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(ms, cancellationToken);
            await action.Invoke();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is not null)
            await _cts.CancelAsync();

        _logger.LogInformation("Stopping, disconnecting all peers");
        _peerManager.disconnect_all_peers();

        _descriptors.CollectionChanged -= DescriptorsOnCollectionChanged;
    }

    private async Task ListenForInboundConnections(CancellationToken cancellationToken = default)
    {
        using var listener = new TcpListener(new IPEndPoint(IPAddress.Any, 0));
        listener.Start();
        var ip = listener.LocalEndpoint;
        Endpoint = new IPEndPoint(IPAddress.Loopback, (int) ip.Port());
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = LDKTcpDescriptor.Inbound(_peerManager, await listener.AcceptTcpClientAsync(cancellationToken),
                _logger, _descriptors);
            if (result is not null)
            {
                _descriptors.TryAdd(result.Id, result);
                _peerManager.process_events();
            }
        }
    }

    public EndPoint Endpoint { get; set; }

    public async Task<LDKTcpDescriptor?> ConnectAsync(NodeInfo nodeInfo,
        CancellationToken cancellationToken = default)
    {
        var remote = IPEndPoint.Parse(nodeInfo.Host + ":" + nodeInfo.Port);
        return await ConnectAsync(nodeInfo.NodeId, remote, cancellationToken);
    }

    public async Task<LDKTcpDescriptor?> ConnectAsync(PubKey theirNodeId, EndPoint remote,
        CancellationToken cancellationToken = default)
    {
        if (_channelManager.get_our_node_id().SequenceEqual(theirNodeId.ToBytes()))
            return null;

        var client = new TcpClient();
        await client.ConnectAsync(remote.IPEndPoint(), cancellationToken);
        var result = LDKTcpDescriptor.Outbound(_peerManager, client, _logger, theirNodeId, _descriptors);
        if (result is not null)
        {
            _descriptors.TryAdd(result.Id, result);
            _peerManager.process_events();
        }

        return result;
    }

    private SemaphoreSlim peerFetchLock = new(1, 1);
    private HashSet<string> _peerNodeIds = new();

    public async Task<List<NodeInfo>> GetPeerNodeIds()
    {
        // update the peer node ids in _peerrNodeIds with the latest from the peer manager without recrerating the observable
        await peerFetchLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var peerNodeIds = _peerManager.get_peer_node_ids().Select(zz =>
                {
                    var pubKey = new PubKey(zz.get_a());
                    var addr = zz.get_b() is Option_SocketAddressZ.Option_SocketAddressZ_Some x
                        ? x.some.to_str()
                        : null;
                    EndPointParser.TryParse(addr, 9735, out var endpoint);
                    return new NodeInfo(pubKey, endpoint.Host(), endpoint.Port().Value);
                }).ToList();

                var updated = false;
                foreach (var peerNodeId in peerNodeIds)
                {
                    if (_peerNodeIds.Add(peerNodeId.NodeId.ToString()))
                    {
                        updated = true;
                    }
                }

                if (_peerNodeIds.RemoveWhere(x => peerNodeIds.All(y => y.NodeId.ToString() != x)) > 0)
                {
                    updated = true;
                }

                if (updated)
                {
                    OnPeersChange?.Invoke(this, new PeersChangedEventArgs(peerNodeIds));
                    _peerNodeIds = [..peerNodeIds.Select(x => x.NodeId.ToString())];
                    _logger.LogInformation("Peers changed");
                }

                return peerNodeIds;
            });
        }
        finally
        {
            peerFetchLock.Release();
        }
    }
}

public class PeersChangedEventArgs : EventArgs
{
    public List<NodeInfo> PeerNodeIds { get; set; }

    public PeersChangedEventArgs(List<NodeInfo> peerNodeIds)
    {
        PeerNodeIds = peerNodeIds;
    }
}

public class LDKNode : IAsyncDisposable, IHostedService
{
    private readonly CurrentWalletService _currentWalletService;
    private readonly ILogger _logger;
    private readonly ChannelManager _channelManager;
    private readonly LDKPeerHandler _peerHandler;

    public LDKNode(IServiceProvider serviceProvider,
        CurrentWalletService currentWalletService, LDKWalletLogger logger, ChannelManager channelManager, LDKPeerHandler peerHandler)
    {
        _currentWalletService = currentWalletService;
        _logger = logger;
        _channelManager = channelManager;
        _peerHandler = peerHandler;
        ServiceProvider = serviceProvider;
    }

    public PubKey NodeId => new(_channelManager.get_our_node_id());
    
    public NodeInfo? NodeInfo => _peerHandler.Endpoint is null ? null :  NodeInfo.Parse($"{NodeId}@{_peerHandler.Endpoint}");
    

    public event EventHandler OnDisposing;


    public IServiceProvider ServiceProvider { get; }
    private TaskCompletionSource? _started = null;
    private static readonly SemaphoreSlim Semaphore = new(1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _currentWalletService.WalletSelected.Task;

        bool exists;
        try
        {
await Semaphore.WaitAsync(cancellationToken);
            exists = _started is not null;
            _started ??= new TaskCompletionSource();
        }
        finally
        {
            Semaphore.Release();
        }
        if (exists)
        {
            await _started.Task;
            return;
        }


        var services = ServiceProvider.GetServices<IScopedHostedService>();
        
        _logger.LogInformation("Starting LDKNode services" );
        foreach (var service in services)
        {
            await service.StartAsync(cancellationToken);
        }
        _started.SetResult();
        _logger.LogInformation("LDKNode started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        bool exists;
        try
        {
            await Semaphore.WaitAsync(cancellationToken);
            exists = _started is not null;
        }
        finally
        {
            Semaphore.Release();
        }
        if (!exists)
            return;

        var services = ServiceProvider.GetServices<IScopedHostedService>();
        foreach (var service in services)
        {
            await service.StopAsync(cancellationToken);
        }
    }

    public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
        OnDisposing?.Invoke(this, EventArgs.Empty);
    }
}



public static class ChannelManagerHelper
{

    public static ChannelMonitor[] GetInitialMonitors(byte[][] channelMonitorsSerialized, EntropySource entropySource, SignerProvider signerProvider )
    {
        var monitorFundingSet = new HashSet<OutPoint>();
        return channelMonitorsSerialized.Select(bytes =>
        {
            if (UtilMethods.C2Tuple_ThirtyTwoBytesChannelMonitorZ_read(bytes, entropySource,
                    signerProvider) is not Result_C2Tuple_ThirtyTwoBytesChannelMonitorZDecodeErrorZ.Result_C2Tuple_ThirtyTwoBytesChannelMonitorZDecodeErrorZ_OK res)
            {
                throw new SerializationException("Serialized ChannelMonitor was corrupt");
            }
            var monitor = res.res.get_b();

            if (!monitorFundingSet.Add(monitor.get_funding_txo().get_a()))
            {
                throw new SerializationException("Set of ChannelMonitors contained duplicates (ie the same funding_txo was set on multiple monitors)");
            }
            return monitor;

        }).ToArray();
    }
    
    
    public static  ChannelManager? Load(ChannelMonitor[] channelMonitors, byte[]? channelManagerSerialized, 
        EntropySource entropySource, SignerProvider signerProvider, 
        NodeSigner nodeSigner, FeeEstimator feeEstimator, 
        Watch watch, BroadcasterInterface txBroadcaster, 
        Router router, Logger logger, UserConfig config, Filter filter)
    {
        var resManager = UtilMethods.C2Tuple_ThirtyTwoBytesChannelManagerZ_read(channelManagerSerialized, entropySource, 
            nodeSigner, signerProvider, feeEstimator, 
            watch, txBroadcaster, 
            router, logger, config, channelMonitors);
        if (!resManager.is_ok())
        {
            throw new SerializationException("Serialized ChannelManager was corrupt");
        }

        foreach (var monitor in channelMonitors)
        {
            monitor.load_outputs_to_watch(filter, logger);
        }
        return (resManager as Result_C2Tuple_ThirtyTwoBytesChannelManagerZDecodeErrorZ.Result_C2Tuple_ThirtyTwoBytesChannelManagerZDecodeErrorZ_OK)?.res.get_b();
    }
}

public class CurrentWalletService
{
    private readonly WalletService _walletService;

    private WalletConfig? _wallet;
    public CurrentWalletService( WalletService walletService)
    {
        _walletService = walletService;
    }

    public void SetWallet(WalletConfig wallet)
    {
        if (_wallet is not null)
        {
            throw new InvalidOperationException("wallet is already selected");
        }
        _wallet = wallet;
      
        WalletSelected.SetResult();

    }
    

    public byte[] Seed { get; private set; }

    public string CurrentWallet
    {
        get
        {
            if (_wallet is null)
                throw new InvalidOperationException("No wallet selected");
            return _wallet.Fingerprint;
        }
    }

    public TaskCompletionSource WalletSelected { get; } = new();

    private readonly TaskCompletionSource<ChannelMonitor[]?> icm = new();
    public async Task<ChannelMonitor[]> GetInitialChannelMonitors()
    {
        return await icm.Task;
    }
    private async Task<ChannelMonitor[]> GetInitialChannelMonitors(EntropySource entropySource, SignerProvider signerProvider)
    {
        await WalletSelected.Task;
        var data = _wallet.Channels?.Select(c => c.Data)?.ToArray() ?? Array.Empty<byte[]>();
        var channels = ChannelManagerHelper.GetInitialMonitors(data, entropySource, signerProvider);
        icm.SetResult(channels);
        return channels;
    }

    public async Task<(byte[] serializedChannelManager, ChannelMonitor[] channelMonitors)?> GetSerializedChannelManager(EntropySource entropySource, SignerProvider signerProvider)
    {
        await WalletSelected.Task;
        var data= await _walletService.GetArbitraryData<byte[]>("ChannelManager", CurrentWallet);
        if (data is null)
        {
            icm.SetResult(Array.Empty<ChannelMonitor>());
            return null;
        }

        var channels = await GetInitialChannelMonitors(entropySource, signerProvider);
        return (data, channels);
    }
}